using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>
/// <see cref="IReminderRepository"/>: reads only the Notifications tables (templates and stored notifications) without
/// tracking; notifications are only added.
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
}
