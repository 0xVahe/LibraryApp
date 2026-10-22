using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Services;

/// <summary>
/// Administration of the notification templates (NOT-09). A template may use only the placeholders that have a value for
/// its type; at most one template of a type is active, and the active one cannot be deleted, so every type can always be sent.
/// </summary>
public interface INotificationTemplateService
{
    /// <summary>All templates, or those of one type, ordered by type with the active one first.</summary>
    Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(NotificationType? type, CancellationToken ct = default);

    /// <exception cref="Domain.Common.DomainException"><c>notifications.template_does_not_exist</c> (404).</exception>
    Task<NotificationTemplateDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates an inactive template. Throws <c>notifications.template_malformed_placeholder</c> and <c>notifications.template_unknown_placeholder</c> (400).</summary>
    Task<NotificationTemplateDto> CreateAsync(CreateNotificationTemplateRequest request, CancellationToken ct = default);

    /// <summary>Changes the subject and body. Throws <c>notifications.template_does_not_exist</c> (404) and the placeholder errors (400).</summary>
    Task<NotificationTemplateDto> UpdateAsync(Guid id, UpdateNotificationTemplateRequest request, CancellationToken ct = default);

    /// <summary>Makes the template the active one of its type; the previous active one is deactivated. Repeating it changes nothing.</summary>
    Task<NotificationTemplateDto> ActivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes an inactive template. The active one is refused with <c>notifications.template_active_cannot_be_deleted</c> (422).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Renders a template text with example values, the way an employee would receive it. Stores nothing.</summary>
    NotificationTemplatePreviewDto Preview(PreviewNotificationTemplateRequest request);
}
