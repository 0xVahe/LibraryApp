namespace LibraryApp.Application.Notifications.Services;

/// <summary>Sends Pending Outlook notifications and marks each one Sent or Failed (NOT-03).</summary>
public interface INotificationDeliveryService
{
    /// <summary>Claims one batch of Pending notifications and sends them; returns how many were claimed.</summary>
    Task<int> DeliverPendingAsync(CancellationToken ct = default);
}
