using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// The loans of the 09:00 reminders (NOT-05), from the modules that own the data: the loans from Borrowing
/// (<see cref="IBorrowingReadService"/>), the book title from Catalog (<see cref="ICatalogService"/>), the recipient from
/// <see cref="INotificationRecipientReader"/> and the status from Staff (<see cref="IStaffMemberService"/>). Loans of
/// staff who left the bank (Deactivated) are left out.
/// </summary>
public interface IReminderLoanReader
{
    /// <summary>Open loans (Active or Overdue) due on these local dates, earliest due first.</summary>
    Task<IReadOnlyList<ReminderLoan>> GetLoansDueOnAsync(IReadOnlyCollection<DateOnly> localDates, CancellationToken ct = default);

    /// <summary>Loans that Borrowing has marked Overdue (its rule <c>Borrowing:Loans:OverdueFrom</c>), earliest due first.</summary>
    Task<IReadOnlyList<ReminderLoan>> GetOverdueLoansAsync(CancellationToken ct = default);
}

public sealed record ReminderLoan(
    Guid LoanId,
    Guid StaffMemberId,
    string EmployeeName,
    string CorporateEmail,
    string? LineManagerEmail,
    string BookTitle,
    DateTime DueAtUtc);

public sealed class ReminderLoanReader(
    IBorrowingReadService borrowing,
    ICatalogService catalog,
    INotificationRecipientReader recipients,
    IStaffMemberService staff) : IReminderLoanReader
{
    public async Task<IReadOnlyList<ReminderLoan>> GetLoansDueOnAsync(
        IReadOnlyCollection<DateOnly> localDates, CancellationToken ct = default)
    {
        List<OpenLoanInfo> loans = [];
        foreach (DateOnly date in localDates)
            loans.AddRange(await borrowing.GetLoansDueOnAsync(date, ct));

        return await ToReminderLoansAsync(loans.OrderBy(loan => loan.DueAtUtc).ToList(), ct);
    }

    public async Task<IReadOnlyList<ReminderLoan>> GetOverdueLoansAsync(CancellationToken ct = default) =>
        await ToReminderLoansAsync(await borrowing.GetOverdueLoansAsync(ct), ct);

    // One call per staff member and per book, not per loan.
    private async Task<IReadOnlyList<ReminderLoan>> ToReminderLoansAsync(IReadOnlyList<OpenLoanInfo> loans, CancellationToken ct)
    {
        Dictionary<Guid, NotificationRecipient> recipientOf = [];
        foreach (Guid staffMemberId in loans.Select(loan => loan.StaffMemberId).Distinct())
        {
            NotificationRecipient? recipient = await recipients.FindAsync(staffMemberId, ct);
            if (recipient is not null && await staff.GetStatusAsync(staffMemberId, ct) != StaffMemberStatus.Deactivated)
                recipientOf[staffMemberId] = recipient;
        }

        Dictionary<Guid, string> titleOf = [];
        foreach (Guid bookId in loans.Where(loan => recipientOf.ContainsKey(loan.StaffMemberId)).Select(loan => loan.BookId).Distinct())
        {
            // A soft-deleted book is still returned, so a loan of it is still reminded.
            if (await catalog.GetBookAsync(bookId, ct) is { } book)
                titleOf[bookId] = book.Title;
        }

        return loans
            .Where(loan => recipientOf.ContainsKey(loan.StaffMemberId) && titleOf.ContainsKey(loan.BookId))
            .Select(loan =>
            {
                NotificationRecipient recipient = recipientOf[loan.StaffMemberId];
                return new ReminderLoan(
                    loan.LoanId,
                    loan.StaffMemberId,
                    recipient.FullName,
                    recipient.Email,
                    recipient.LineManagerEmail,
                    titleOf[loan.BookId],
                    loan.DueAtUtc);
            })
            .ToList();
    }
}
