using LibraryApp.Application.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Storage operations of the notifications. Saving belongs to the unit of work.</summary>
public interface INotificationRepository
{
    /// <summary>
    /// A page of the in-app notifications of one staff member, unread first, then newest first (NOT-04).
    /// Read only.
    /// </summary>
    Task<PagedResult<Notification>> SearchInAppAsync(
        Guid staffMemberId,
        bool unreadOnly,
        PageRequest paging,
        CancellationToken ct = default);

    /// <summary>
    /// The notification with this id when it belongs to this staff member, tracked so a change is saved by the unit of
    /// work; null when there is none or it is another person's.
    /// </summary>
    Task<Notification?> FindOwnedAsync(Guid notificationId, Guid staffMemberId, CancellationToken ct = default);

    /// <summary>Adds a new notification; it is saved by the unit of work (event handlers run inside one).</summary>
    Task AddAsync(Notification notification, CancellationToken ct = default);

    /// <summary>
    /// True when this event already created a notification of this type on this channel (G4-09: a repeated event is
    /// ignored; the same triple is unique in the database).
    /// </summary>
    Task<bool> ExistsForEventAsync(Guid eventId, NotificationType type, NotificationChannel channel, CancellationToken ct = default);

    /// <summary>
    /// True when a notification of this type already exists for this loan on this channel on this Yerevan day
    /// (NOT-06, G4-02; the same four values are unique in the database).
    /// </summary>
    Task<bool> ExistsForLoanAsync(
        Guid loanId, NotificationType type, NotificationChannel channel, DateOnly localDate, CancellationToken ct = default);
}
