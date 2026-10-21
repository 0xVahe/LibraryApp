using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Application.Notifications.Options;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Notifications.Services;

/// <inheritdoc cref="INotificationDeliveryService"/>
public sealed class NotificationDeliveryService(
    INotificationDeliveryStore store,
    IOutlookNotificationSender sender,
    IClock clock,
    IOptions<NotificationsOptions> options,
    ILogger<NotificationDeliveryService> logger) : INotificationDeliveryService
{
    public async Task<int> DeliverPendingAsync(CancellationToken ct = default)
    {
        DeliveryOptions settings = options.Value.Delivery;
        DateTime now = clock.UtcNow;
        var claimed = await store.ClaimDueAsync(now, now + settings.Lease, settings.BatchSize, ct);

        foreach (var notification in claimed)
        {
            await DeliverAsync(notification, ct);
            // One save per email, so a later failure does not undo it. If this save fails, the email goes out again
            // when the lease ends.
            await store.SaveAsync(ct);
        }

        return claimed.Count;
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
