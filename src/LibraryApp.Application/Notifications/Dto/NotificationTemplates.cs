using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Notifications.Dto;

/// <summary>An editable notification template (NOT-09). At most one template of a type is active.</summary>
/// <param name="Id">Template id.</param>
/// <param name="Type">The notification type the template is for.</param>
/// <param name="Subject">Plain text with {{placeholders}}, at most 300 characters.</param>
/// <param name="BodyTemplate">HTML fragment with {{placeholders}}; the email layout is added when it is rendered.</param>
/// <param name="IsActive">True for the one template of the type that emails are made from.</param>
/// <param name="AllowedPlaceholders">The placeholders a template of this type may use.</param>
public sealed record NotificationTemplateDto(
    Guid Id,
    NotificationType Type,
    string Subject,
    string BodyTemplate,
    bool IsActive,
    IReadOnlyList<string> AllowedPlaceholders);

/// <summary>Creates a template (NOT-09). A new template is inactive until it is activated.</summary>
/// <param name="Type">Required: the notification type the template is for.</param>
/// <param name="Subject">Required, up to 300 characters after trimming.</param>
/// <param name="BodyTemplate">Required.</param>
public sealed record CreateNotificationTemplateRequest(NotificationType? Type, string? Subject, string? BodyTemplate);

/// <summary>Changes the text of a template (NOT-09); the type does not change.</summary>
/// <param name="Subject">Required, up to 300 characters after trimming.</param>
/// <param name="BodyTemplate">Required.</param>
public sealed record UpdateNotificationTemplateRequest(string? Subject, string? BodyTemplate);

/// <summary>A template text to preview without saving it (NOT-09).</summary>
/// <param name="Type">Required: decides which placeholders are allowed and which example values are used.</param>
/// <param name="Subject">Required, up to 300 characters after trimming.</param>
/// <param name="BodyTemplate">Required.</param>
public sealed record PreviewNotificationTemplateRequest(NotificationType? Type, string? Subject, string? BodyTemplate);

/// <summary>The rendered preview: the subject and the full HTML email, as an employee would receive it (NOT-09).</summary>
/// <param name="Subject">The subject with the example values put in, on one line.</param>
/// <param name="HtmlBody">The body with the example values put in, inside the Ameriabank email layout.</param>
public sealed record NotificationTemplatePreviewDto(string Subject, string HtmlBody);
