namespace LibraryApp.Domain.Notifications;

/// <summary>
/// Where a notification is delivered. "Outlook + in-app" is stored as one row per channel.
/// Explicit values starting at 1, so 0 is never a valid value.
/// </summary>
public enum NotificationChannel
{
    /// <summary>Corporate Outlook email (smtp4dev in training).</summary>
    Outlook = 1,

    /// <summary>In-app notification list (GET /api/notifications/me).</summary>
    InApp = 2,
}
