using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace LibraryApp.Application.Notifications.Services;

/// <inheritdoc cref="INotificationDeliveryService"/>
public sealed class NotificationDeliveryService(
    INotificationDeliveryStore store,
    IOutlookNotificationSender sender,
    IClock clock,
    ILogger<NotificationDeliveryService> logger) : INotificationDeliveryService
{
    public async Task<int> DeliverPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var pending = await store.GetPendingAsync(batchSize, ct);

        foreach (var notification in pending)
        {
            await DeliverAsync(notification, ct);
            // One save per email: a sent email is never sent again because a later one failed.
            await store.SaveAsync(ct);
        }

        return pending.Count;
    }

    private async Task DeliverAsync(Notification notification, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(
                new OutlookMessage(notification.RecipientEmail, notification.CcEmail, notification.Subject, notification.Body), ct);
            notification.MarkSent(clock.UtcNow);
        }
        catch (NotificationDeliveryException ex)
        {
            // A failed email stays Failed; it is not sent again.
            notification.MarkFailed(ex.Message, null);

            // Ids only: the recipient is personal data (NFR-06).
            logger.LogError("Notification {NotificationId} not delivered: {Error}", notification.Id, ex.Message);
        }
    }
}
