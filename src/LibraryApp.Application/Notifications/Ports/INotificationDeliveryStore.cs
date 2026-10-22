using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Outlook notifications waiting for delivery (NOT-03) or for a retry (NOT-08).</summary>
public interface INotificationDeliveryStore
{
    /// <summary>
    /// Reserves up to <paramref name="max"/> due Outlook notifications (Pending, or Failed with a retry time that has come)
    /// until <paramref name="leaseUntilUtc"/> in one atomic step, oldest first, and returns them tracked. Another API
    /// instance does not get them while the lease lasts.
    /// </summary>
    Task<IReadOnlyList<Notification>> ClaimDueAsync(DateTime nowUtc, DateTime leaseUntilUtc, int max, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}
