using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Reads the editable notification templates (NOT-02).</summary>
public interface INotificationTemplateRepository
{
    /// <summary>The active template of this type (at most one exists, G4-08), or null. Read only.</summary>
    Task<NotificationTemplate?> FindActiveAsync(NotificationType type, CancellationToken ct = default);
}
