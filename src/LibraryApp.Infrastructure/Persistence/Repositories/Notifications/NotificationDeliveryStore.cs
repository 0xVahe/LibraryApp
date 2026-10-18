using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Notifications;

public sealed class NotificationDeliveryStore(LibraryDbContext db) : INotificationDeliveryStore
{
    public async Task<IReadOnlyList<Notification>> GetPendingAsync(int max, CancellationToken ct = default) =>
        await db.Notifications
            .Where(n => n.Channel == NotificationChannel.Outlook && n.Status == NotificationStatus.Pending)
            .OrderBy(n => n.CreatedAtUtc)
            .ThenBy(n => n.Id)
            .Take(max)
            .ToListAsync(ct);

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
