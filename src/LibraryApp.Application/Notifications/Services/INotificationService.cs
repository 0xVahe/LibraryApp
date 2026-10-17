using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Dto;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>In-app notifications of the current staff member (NOT-04). Always the caller's own (NFR-03).</summary>
public interface INotificationService
{
    /// <summary>A page of the caller's in-app notifications, unread first, then newest first.</summary>
    Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, PageRequest paging, CancellationToken ct = default);

    /// <summary>
    /// Marks one of the caller's notifications as read. Repeating it keeps the first read time. Another person's
    /// notification is reported as not found.
    /// </summary>
    Task<NotificationDto> MarkReadAsync(Guid notificationId, CancellationToken ct = default);
}
