using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>EF Core storage of the notification templates. It never saves; the unit of work commits the operation.</summary>
public sealed class NotificationTemplateRepository(LibraryDbContext db) : INotificationTemplateRepository
{
    public Task<NotificationTemplate?> FindActiveAsync(NotificationType type, CancellationToken ct = default) =>
        db.NotificationTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Type == type && t.IsActive, ct);

    public async Task<IReadOnlyList<NotificationTemplate>> ListAsync(NotificationType? type, CancellationToken ct = default)
    {
        IQueryable<NotificationTemplate> query = db.NotificationTemplates.AsNoTracking();
        if (type is { } wanted)
            query = query.Where(t => t.Type == wanted);

        return await query.OrderBy(t => t.Type).ThenByDescending(t => t.IsActive).ThenBy(t => t.Subject).ThenBy(t => t.Id).ToListAsync(ct);
    }

    public Task<NotificationTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotificationTemplates.SingleOrDefaultAsync(t => t.Id == id, ct);

    public void Add(NotificationTemplate template) => db.NotificationTemplates.Add(template);

    public void Remove(NotificationTemplate template) => db.NotificationTemplates.Remove(template);

    // A statement of its own, run at once in the current transaction: the unique index (one active per type) is checked per
    // statement, so the old one must be inactive before the new one is saved as active.
    public Task DeactivateOthersAsync(NotificationType type, Guid exceptId, CancellationToken ct = default) =>
        db.NotificationTemplates
            .Where(t => t.Type == type && t.Id != exceptId && t.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.IsActive, false), ct);
}
