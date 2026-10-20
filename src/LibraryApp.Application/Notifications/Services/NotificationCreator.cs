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
         Guid? loanId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(values);
        ct.ThrowIfCancellationRequested();

        // Checked before any query: a wrong call is a programming error, not something to retry.
        var wanted = channels.Distinct().ToList();
        if (wanted.Count == 0 || wanted.Any(channel => !Enum.IsDefined(channel)))
            throw new ArgumentException("At least one defined notification channel is required.", nameof(channels));

        DateTime nowUtc = clock.UtcNow;
        DateOnly localDate = clock.ToLocalDate(nowUtc);

        // The inbox already skips a handler that succeeded; this also covers a row that exists for another reason (G4-09).
        // Per channel, like the unique indexes: only the missing channels are created. A loan notification is also
        // skipped when its type was already created for the loan on this day (NOT-06); checked here because a unique
        // violation would fail the handler and be retried.
        var missing = new List<NotificationChannel>();
        foreach (NotificationChannel channel in wanted)
        {
            if (await notifications.ExistsForEventAsync(eventId, type, channel, ct))
                continue;
            if (loanId is { } loan && await notifications.ExistsForLoanAsync(loan, type, channel, localDate, ct))
                continue;

            missing.Add(channel);
        }

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

        Notification Create(NotificationChannel channel) => Notification.Create(
            ids.NewId(), staffMemberId, type, channel, recipient.Email, ccEmail: null,
            rendered.Subject, rendered.HtmlBody, nowUtc, localDate, loanId, eventId);

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
