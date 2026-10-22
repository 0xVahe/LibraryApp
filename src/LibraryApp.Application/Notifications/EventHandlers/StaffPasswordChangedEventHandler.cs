using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the PasswordChanged security notice when a staff member changes their password (NOT-01, MEM-02).
/// </summary>
/// <remarks>
/// The channels are an ASSUMPTION: the notification catalogue (requirements, section 9) has no row for this type, so
/// Outlook + in-app is taken as the general rule of the catalogue (G4-05). Change <see cref="Channels"/> once the owner of
/// the requirements decides.
/// </remarks>
public sealed class StaffPasswordChangedEventHandler(INotificationCreator creator) : IEventHandler<StaffPasswordChanged>
{
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook, NotificationChannel.InApp];

    // The PasswordChanged template only uses {{EmployeeName}}, which the creator fills from the recipient.
    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    public Task HandleAsync(StaffPasswordChanged domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        return creator.CreateAsync(
            NotificationType.PasswordChanged, domainEvent.StaffMemberId, domainEvent.EventId, Channels, NoValues,
            ct: ct);
    }
}
