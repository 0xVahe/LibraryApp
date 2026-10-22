using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>
/// <see cref="IReminderRepository"/>: loans, staff and books are read in one query without tracking (read only, never
/// written); notifications are only added.
/// </summary>
public sealed class ReminderRepository(LibraryDbContext db) : IReminderRepository
{
    public async Task<IReadOnlyList<ReminderTemplate>> GetTemplatesAsync(
        IReadOnlyCollection<NotificationType> types,
        CancellationToken ct = default) =>
        await db.NotificationTemplates.AsNoTracking()
            .Where(t => t.IsActive && types.Contains(t.Type))
            .Select(t => new ReminderTemplate(t.Type, t.Subject, t.BodyTemplate))
            .ToListAsync(ct);

    public Task<IReadOnlyList<ReminderLoan>> GetOpenLoansDueAsync(
        DateTime dueFromUtc,
        DateTime dueBeforeUtc,
        CancellationToken ct = default) =>
        GetLoansAsync(
            db.Loans.Where(loan => (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
                                   && loan.DueAtUtc >= dueFromUtc
                                   && loan.DueAtUtc < dueBeforeUtc),
            ct);

    public Task<IReadOnlyList<ReminderLoan>> GetOverdueLoansAsync(CancellationToken ct = default) =>
        GetLoansAsync(db.Loans.Where(loan => loan.Status == LoanStatus.Overdue), ct);

    public async Task<IReadOnlySet<(Guid LoanId, NotificationType Type)>> GetLoanNotificationsOnAsync(
        DateOnly localDate,
        IReadOnlyCollection<NotificationType> types,
        CancellationToken ct = default)
    {
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.LocalDate == localDate && n.LoanId != null && types.Contains(n.Type))
            .Select(n => new { LoanId = n.LoanId!.Value, n.Type })
            .Distinct()
            .ToListAsync(ct);

        return rows.Select(row => (row.LoanId, row.Type)).ToHashSet();
    }

    public async Task<IReadOnlyDictionary<(Guid LoanId, NotificationType Type), DateOnly>> GetLastLoanNotificationDatesAsync(
        IReadOnlyCollection<Guid> loanIds,
        IReadOnlyCollection<NotificationType> types,
        DateOnly beforeDate,
        CancellationToken ct = default)
    {
        if (loanIds.Count == 0)
            return new Dictionary<(Guid, NotificationType), DateOnly>();

        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.LoanId != null && loanIds.Contains(n.LoanId.Value) && types.Contains(n.Type)
                        && n.LocalDate < beforeDate)
            .GroupBy(n => new { LoanId = n.LoanId!.Value, n.Type })
            .Select(g => new { g.Key.LoanId, g.Key.Type, LocalDate = g.Max(n => n.LocalDate) })
            .ToListAsync(ct);

        return rows.ToDictionary(row => (row.LoanId, row.Type), row => row.LocalDate);
    }

    public void Add(Notification notification) => db.Notifications.Add(notification);

    private async Task<IReadOnlyList<ReminderLoan>> GetLoansAsync(IQueryable<Loan> loans, CancellationToken ct) => await (
            from loan in loans.AsNoTracking()
            join staff in db.StaffMembers.AsNoTracking() on loan.StaffMemberId equals staff.Id
            where staff.Status != StaffMemberStatus.Deactivated
            join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
            join book in db.Books.AsNoTracking() on copy.BookId equals book.Id
            orderby loan.DueAtUtc, loan.Id
            select new ReminderLoan(
                loan.Id,
                staff.Id,
                staff.FirstName + " " + staff.LastName,
                staff.CorporateEmail,
                staff.LineManagerEmail,
                book.Title,
                loan.DueAtUtc))
            .ToListAsync(ct);
}
