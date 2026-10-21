using System.Globalization;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the PenaltyApplied notification when penalty points are added (NOT-01). By NOT-06 a second penalty of the
/// same loan on the same Yerevan day gets no second notification.
/// </summary>
public sealed class PenaltyAppliedEventHandler(INotificationCreator creator) : IEventHandler<PenaltyApplied>
{
    // Channels of PenaltyApplied in the notification catalogue (requirements, section 9): Outlook + in-app.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook, NotificationChannel.InApp];

    public Task HandleAsync(PenaltyApplied domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        var values = new Dictionary<string, string>
        {
            [NotificationPlaceholders.Points] = domainEvent.Points.ToString(CultureInfo.InvariantCulture),
            [NotificationPlaceholders.TotalPoints] = domainEvent.ActivePointsTotal.ToString(CultureInfo.InvariantCulture),
            [NotificationPlaceholders.Reason] = domainEvent.Reason,
        };

        return creator.CreateAsync(
            NotificationType.PenaltyApplied, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
            domainEvent.LoanId, ct: ct);
    }
}
