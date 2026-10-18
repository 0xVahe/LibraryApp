using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Outlook notifications waiting for delivery (NOT-03).</summary>
public interface INotificationDeliveryStore
{
    /// <summary>Pending Outlook notifications, oldest first, tracked so that their status can be saved.</summary>
    Task<IReadOnlyList<Notification>> GetPendingAsync(int max, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}
