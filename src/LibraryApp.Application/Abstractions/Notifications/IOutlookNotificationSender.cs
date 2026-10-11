namespace LibraryApp.Application.Abstractions.Notifications;

/// <summary>Sends an email (INT-08, NOT-03). Training: MailKit to smtp4dev; production: Microsoft Graph (C-3).</summary>
public interface IOutlookNotificationSender
{
    /// <exception cref="NotificationDeliveryException">The mail server did not accept the message.</exception>
    Task SendAsync(OutlookMessage message, CancellationToken ct = default);
}
