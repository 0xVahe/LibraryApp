using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Notifications.EventHandlers;

/// <summary>
/// Creates the AccessRestricted or AccessSuspended notice when the standing of a staff member gets worse (NOT-01).
/// Only Outlook (notification catalogue, section 9); a suspension is also copied to the line manager.
/// <para>
/// Only a change for the worse is told: Active to Restricted, and Restricted or Active to Suspended. An improvement
/// (Suspended to Restricted, Restricted to Active - these come from the daily penalty recalculation) and Deactivated
/// have no notification type, so they are ignored. Return and lost-copy flows only ever make the standing worse, and
/// the daily recalculation only better, so the direction of the change is enough to tell them apart.
/// </para>
/// </summary>
public sealed class StaffStatusChangedEventHandler(INotificationCreator creator, IClock clock)
    : IEventHandler<StaffStatusChanged>
{
    private static readonly NotificationChannel[] Channels = [NotificationChannel.Outlook];

    public Task HandleAsync(StaffStatusChanged domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ct.ThrowIfCancellationRequested();

        if (domainEvent.OldStatus == StaffMemberStatus.Deactivated
            || Rank(domainEvent.NewStatus) <= Rank(domainEvent.OldStatus))
            return Task.CompletedTask;

        var values = new Dictionary<string, string> { [NotificationPlaceholders.Reason] = domainEvent.Reason };

        switch (domainEvent.NewStatus)
        {
            case StaffMemberStatus.Restricted:
                return creator.CreateAsync(
                    NotificationType.AccessRestricted, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
                    ct: ct);

            case StaffMemberStatus.Suspended:
                // The template needs the end of the suspension; a suspension that starts always carries it.
                if (domainEvent.EffectiveUntilUtc is not { } until)
                    throw new DomainException(NotificationErrors.EffectiveUntilRequired);

                values[NotificationPlaceholders.EffectiveUntil] =
                    NotificationPlaceholders.FormatDate(clock.ToLocalDate(until));
                return creator.CreateAsync(
                    NotificationType.AccessSuspended, domainEvent.StaffMemberId, domainEvent.EventId, Channels, values,
                    ccLineManager: true, ct: ct);

            default:
                return Task.CompletedTask;
        }
    }

    // Deactivated is not part of the order: no notice exists for it (rank 0 means "not told").
    private static int Rank(StaffMemberStatus status) => status switch
    {
        StaffMemberStatus.Active => 1,
        StaffMemberStatus.Restricted => 2,
        StaffMemberStatus.Suspended => 3,
        _ => 0,
    };
}
