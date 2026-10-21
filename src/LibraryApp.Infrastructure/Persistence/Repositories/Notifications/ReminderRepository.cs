using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>
///  loans, staff and books are read in one query without tracking (read only, never
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

    public async Task<IReadOnlyList<ReminderLoan>> GetOpenLoansDueAsync(
        DateTime? dueFromUtc,
        DateTime dueBeforeUtc,
        CancellationToken ct = default) => await (
            from loan in db.Loans.AsNoTracking()
            where (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
                  && (dueFromUtc == null || loan.DueAtUtc >= dueFromUtc)
                  && loan.DueAtUtc < dueBeforeUtc
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

    public void Add(Notification notification) => db.Notifications.Add(notification);
}