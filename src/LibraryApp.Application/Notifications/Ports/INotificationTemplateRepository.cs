using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Ports;

/// <summary>Storage of the editable notification templates (NOT-02, NOT-09). Saving belongs to the unit of work.</summary>
public interface INotificationTemplateRepository
{
    /// <summary>The active template of this type (at most one exists, G4-08), or null. Read only.</summary>
    Task<NotificationTemplate?> FindActiveAsync(NotificationType type, CancellationToken ct = default);

    /// <summary>All templates, or those of one type, ordered by type, active first. Read only.</summary>
    Task<IReadOnlyList<NotificationTemplate>> ListAsync(NotificationType? type, CancellationToken ct = default);

    /// <summary>The template with this id, tracked so a change is saved by the unit of work; null when there is none.</summary>
    Task<NotificationTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);

    void Add(NotificationTemplate template);

    void Remove(NotificationTemplate template);

    /// <summary>
    /// Deactivates the other active template of this type at once, inside the current transaction, so the caller can then
    /// activate its own: the database allows one active template per type (G4-08) and checks it per statement.
    /// </summary>
    Task DeactivateOthersAsync(NotificationType type, Guid exceptId, CancellationToken ct = default);
}
