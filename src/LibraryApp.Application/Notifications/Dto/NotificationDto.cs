using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Dto;

/// <summary>
/// An in-app notification as the staff member sees it (NOT-04). Delivery data (recipient, attempts, errors) stays out;
/// <see cref="ReadAtUtc"/> null means unread.
/// </summary>
public sealed record NotificationDto(
    Guid NotificationId,
    NotificationType Type,
    string Subject,
    string Body,
    Guid? LoanId,
    DateTime CreatedAtUtc,
    bool IsRead,
    DateTime? ReadAtUtc);
