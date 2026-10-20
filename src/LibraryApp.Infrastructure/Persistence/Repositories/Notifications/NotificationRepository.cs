using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

/// <summary>EF Core storage for notifications. It never saves; the unit of work commits the operation.</summary>
public sealed class NotificationRepository(LibraryDbContext db) : INotificationRepository
{
    public async Task<PagedResult<Notification>> SearchInAppAsync(
        Guid staffMemberId,
        bool unreadOnly,
        PageRequest paging,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        IQueryable<Notification> query = db.Notifications.AsNoTracking()
            .Where(n => n.StaffMemberId == staffMemberId && n.Channel == NotificationChannel.InApp);

        if (unreadOnly)
            query = query.Where(n => n.ReadAtUtc == null);

        int total = await query.CountAsync(ct);

        // Unread first, then newest first; the id makes the order stable between pages.
        List<Notification> items = await query
            .OrderByDescending(n => n.ReadAtUtc == null)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ThenBy(n => n.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Notification>(items, total, paging.Page, paging.PageSize);
    }

    public Task<Notification?> FindOwnedAsync(Guid notificationId, Guid staffMemberId, CancellationToken ct = default) =>
        db.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId && n.StaffMemberId == staffMemberId, ct);

    public Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        db.Notifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsForEventAsync(Guid eventId, NotificationType type, NotificationChannel channel, CancellationToken ct = default) =>
        db.Notifications.AsNoTracking().AnyAsync(n => n.EventId == eventId && n.Type == type && n.Channel == channel, ct);
}
