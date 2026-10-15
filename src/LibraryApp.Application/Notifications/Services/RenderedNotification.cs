namespace LibraryApp.Application.Notifications.Services;

/// <summary>A rendered email: plain-text subject and full HTML body, ready for Notification.Create (NOT-02).</summary>
public sealed record RenderedNotification(string Subject, string HtmlBody);
