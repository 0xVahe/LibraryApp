namespace LibraryApp.Application.Notifications.Services;

/// <summary>Sends Pending Outlook notifications and marks each one Sent or Failed (NOT-03).</summary>
public interface INotificationDeliveryService
{
    /// <summary>Sends up to <paramref name="batchSize"/> Pending notifications; returns how many were tried.</summary>
    Task<int> DeliverPendingAsync(int batchSize, CancellationToken ct = default);
}
