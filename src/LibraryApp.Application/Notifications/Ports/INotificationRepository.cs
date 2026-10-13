using LibraryApp.Application.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Storage operations used by the notification API. Saving belongs to the unit of work.</summary>
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
}
