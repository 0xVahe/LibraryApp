using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Notifications;

/// <summary>Errors of the Notifications module (SD-04). Codes never change; messages may be reworded.</summary>
public static class NotificationErrors
{
    // Notification
    public static readonly Error InvalidReferenceId =
        Error.Validation("notifications.invalid_reference_id", "A referenced entity identifier must not be empty.");

    public static readonly Error TimestampMustBeUtc =
        Error.Validation("notifications.timestamp_must_be_utc", "The timestamp must be expressed in UTC.");

    public static readonly Error RecipientRequired =
        Error.Validation("notifications.recipient_required", "A recipient email is required.");

    public static readonly Error SubjectRequired =
        Error.Validation("notifications.subject_required", "A subject is required.");

    public static readonly Error SubjectTooLong =
        Error.Validation("notifications.subject_too_long", "The subject must be at most 300 characters.");

    public static readonly Error BodyRequired =
        Error.Validation("notifications.body_required", "A body is required.");

    public static readonly Error AlreadySent =
        Error.BusinessRule("notifications.already_sent", "The notification has already been sent.");

    public static readonly Error OnlyInAppCanBeRead =
        Error.BusinessRule("notifications.only_in_app_can_be_read", "Only an in-app notification can be marked as read.");

    /// <summary>No notification has this id, or it belongs to another staff member (NFR-03).</summary>
    public static readonly Error NotificationNotFound =
        Error.NotFound("notifications.notification_not_found", "The notification does not exist.");

    // Template
    public static readonly Error TemplateSubjectRequired =
        Error.Validation("notifications.template_subject_required", "A template subject is required.");

    public static readonly Error TemplateSubjectTooLong =
        Error.Validation("notifications.template_subject_too_long", "The template subject must be at most 300 characters.");

    public static readonly Error TemplateBodyRequired =
        Error.Validation("notifications.template_body_required", "A template body is required.");
}
