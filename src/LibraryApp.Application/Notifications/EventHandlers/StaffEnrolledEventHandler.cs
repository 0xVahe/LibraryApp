using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>Creates the Welcome notification when a staff member is enrolled (NOT-01).</summary>
public sealed class StaffEnrolledEventHandler(INotificationCreator creator) : IEventHandler<StaffEnrolled>
{
    // Channels of Welcome in the notification catalogue (requirements, section 9): Outlook + in-app.
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook, NotificationChannel.InApp];

    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    public Task HandleAsync(StaffEnrolled domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        // The Welcome template only uses {{EmployeeName}}, which the creator fills from the recipient.
        return creator.CreateAsync(NotificationType.Welcome, domainEvent.StaffMemberId, domainEvent.EventId, Channels, NoValues, ct: ct);
    }
}
