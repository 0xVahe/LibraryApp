using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// The loans of the 09:00 reminders (NOT-05), from the modules that own the data: the loans from Borrowing
/// (<see cref="IBorrowingReadService"/>), the book title from Catalog (<see cref="ICatalogService"/>) and the recipient with
/// the status from Staff (<see cref="IStaffMemberService.GetContactsAsync"/>, one call for all members of the run). Loans of
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

    // One call to Staff for all members of the run and one call per book, not per loan.
    private async Task<IReadOnlyList<ReminderLoan>> ToReminderLoansAsync(IReadOnlyList<OpenLoanInfo> loans, CancellationToken ct)
    {
        // The contact carries the status, so Deactivated members (left the bank) are left out without a call per member.
        // A member Staff does not know is missing from the result and is left out too.
        List<Guid> staffMemberIds = loans.Select(loan => loan.StaffMemberId).Distinct().ToList();
        Dictionary<Guid, StaffContact> recipientOf = (await staff.GetContactsAsync(staffMemberIds, ct))
            .Where(contact => contact.Value.Status != StaffMemberStatus.Deactivated)
            .ToDictionary(contact => contact.Key, contact => contact.Value);

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
                StaffContact recipient = recipientOf[loan.StaffMemberId];
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
