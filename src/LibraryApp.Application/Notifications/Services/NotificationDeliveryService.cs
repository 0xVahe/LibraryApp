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
            await DeliverAsync(notification, settings.RetryDelays, ct);
            // One save per email, so a later failure does not undo it. Not cancelled when the app stops: the email is
            // already out, and an unsaved Sent would send it again when the lease ends. The host's shutdown timeout and
            // the database command timeout still bound it.
            await store.SaveAsync(CancellationToken.None);
        }

        return claimed.Count;
    }

    private async Task DeliverAsync(Notification notification, TimeSpan[] retryDelays, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(
                new OutlookMessage(notification.RecipientEmail, notification.CcEmail, notification.Subject, notification.Body), ct);
            notification.MarkSent(clock.UtcNow);
        }
        catch (NotificationDeliveryException ex)
        {
            // NOT-08: the n-th failure waits RetryDelays[n-1]; after the last delay there is no retry (null).
            int failedBefore = notification.Attempts;
            DateTime? nextAttempt = failedBefore < retryDelays.Length ? clock.UtcNow + retryDelays[failedBefore] : null;
            notification.MarkFailed(ex.Message, nextAttempt);

            // Ids only: the recipient is personal data (NFR-06).
            logger.LogError(
                "Notification {NotificationId} not delivered (attempt {Attempt}), next attempt {NextAttemptAtUtc}: {Error}",
                notification.Id, notification.Attempts, nextAttempt?.ToString("O") ?? "none", ex.Message);
        }
    }
}
