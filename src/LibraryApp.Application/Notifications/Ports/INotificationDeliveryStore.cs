using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Outlook notifications waiting for delivery (NOT-03).</summary>
public interface INotificationDeliveryStore
{
    /// <summary>
    /// Reserves up to <paramref name="max"/> Pending Outlook notifications until <paramref name="leaseUntilUtc"/> in one
    /// atomic step, oldest first, and returns them tracked. Another API instance does not get them while the lease lasts.
    /// </summary>
    Task<IReadOnlyList<Notification>> ClaimPendingAsync(DateTime nowUtc, DateTime leaseUntilUtc, int max, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}
