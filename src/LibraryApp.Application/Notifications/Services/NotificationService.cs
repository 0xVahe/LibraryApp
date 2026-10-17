using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// The notification API of a staff member (NOT-04). The owner is always taken from the access token, never from the
/// request, so nobody reads or changes another person's notifications (NFR-03).
/// </summary>
public sealed class NotificationService(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock) : INotificationService
{
    public async Task<PagedResult<NotificationDto>> ListAsync(
        bool unreadOnly,
        PageRequest paging,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        // An account without a staff profile has no notifications.
        if (currentUser.StaffMemberId is not { } staffMemberId)
            return PagedResult<NotificationDto>.Empty(paging);

        PagedResult<Notification> page = await notifications.SearchInAppAsync(staffMemberId, unreadOnly, paging, ct);
        return new PagedResult<NotificationDto>(page.Items.Select(ToDto).ToList(), page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<NotificationDto> MarkReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        Guid staffMemberId = currentUser.StaffMemberId
            ?? throw new DomainException(NotificationErrors.NotificationNotFound);

        Notification notification = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Notification found = await notifications.FindOwnedAsync(notificationId, staffMemberId, token)
                ?? throw new DomainException(NotificationErrors.NotificationNotFound);

            found.MarkRead(clock.UtcNow);   // reading twice keeps the first time
            return found;
        }, ct);

        return ToDto(notification);
    }

    private static NotificationDto ToDto(Notification notification) => new(
        notification.Id,
        notification.Type,
        notification.Subject,
        notification.Body,
        notification.LoanId,
        notification.CreatedAtUtc,
        notification.ReadAtUtc is not null,
        notification.ReadAtUtc);
}
