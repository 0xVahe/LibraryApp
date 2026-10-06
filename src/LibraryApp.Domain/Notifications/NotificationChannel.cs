namespace LibraryApp.Domain.Notifications;

/// <summary>
/// Where a notification is delivered. "Outlook + in-app" is stored as one row per channel (G4-05).
/// Explicit values starting at 1 (SD-11).
/// </summary>
public enum NotificationChannel
{
    /// <summary>Corporate Outlook email (smtp4dev in training).</summary>
    Outlook = 1,

    /// <summary>In-app notification list (GET /api/notifications/me).</summary>
    InApp = 2,
}
