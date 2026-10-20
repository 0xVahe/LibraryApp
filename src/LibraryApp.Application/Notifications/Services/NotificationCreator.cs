using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <inheritdoc cref="INotificationCreator"/>
public sealed class NotificationCreator(
    INotificationRepository notifications,
    INotificationTemplateRepository templates,
    INotificationRecipientReader recipients,
    INotificationTemplateRenderer renderer,
    IIdGenerator ids,
    IClock clock) : INotificationCreator
{
    public async Task CreateAsync(
        NotificationType type,
        Guid staffMemberId,
        Guid eventId,
        IReadOnlyCollection<NotificationChannel> channels,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(values);
        ct.ThrowIfCancellationRequested();

        // Checked before any query: a wrong call is a programming error, not something to retry.
        var wanted = channels.Distinct().ToList();
        if (wanted.Count == 0 || wanted.Any(channel => !Enum.IsDefined(channel)))
            throw new ArgumentException("At least one defined notification channel is required.", nameof(channels));

        // The inbox already skips a handler that succeeded; this also covers a row that exists for another reason (G4-09).
        // Per channel, like the unique index (EventId, Type, Channel): only the missing channels are created.
        var missing = new List<NotificationChannel>();
        foreach (NotificationChannel channel in wanted)
            if (!await notifications.ExistsForEventAsync(eventId, type, channel, ct))
                missing.Add(channel);

        if (missing.Count == 0)
            return;

        NotificationRecipient recipient = await recipients.FindAsync(staffMemberId, ct)
            ?? throw new DomainException(NotificationErrors.RecipientNotFound);

        NotificationTemplate template = await templates.FindActiveAsync(type, ct)
            ?? throw new DomainException(NotificationErrors.TemplateNotFound);

        var all = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [NotificationPlaceholders.EmployeeName] = recipient.FullName,
        };
        RenderedNotification rendered = renderer.Render(template.Subject, template.BodyTemplate, all);

        DateTime nowUtc = clock.UtcNow;
        DateOnly localDate = clock.ToLocalDate(nowUtc);

        Notification Create(NotificationChannel channel) => Notification.Create(
            ids.NewId(), staffMemberId, type, channel, recipient.Email, ccEmail: null,
            rendered.Subject, rendered.HtmlBody, nowUtc, localDate, loanId: null, eventId);

        foreach (NotificationChannel channel in missing)
        {
            Notification notification = Create(channel);

            // Email stays Pending for the sender (NOT-08). In-app: there is nothing to send, so it is delivered when stored.
            if (channel == NotificationChannel.InApp)
                notification.MarkSent(nowUtc);

            await notifications.AddAsync(notification, ct);
        }
    }
}
