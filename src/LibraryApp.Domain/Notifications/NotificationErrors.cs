using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Notifications;

/// <summary>Errors of the Notifications module. Codes never change; messages may be reworded.</summary>
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

    /// <summary>The staff member a notification is for does not exist (any more). A permanent failure for the event.</summary>
    public static readonly Error RecipientNotFound =
        Error.NotFound("notifications.recipient_not_found", "The staff member to notify does not exist.");

    /// <summary>The book a notification is about does not exist. A permanent failure for the event.</summary>
    public static readonly Error BookNotFound =
        Error.NotFound("notifications.book_not_found", "The book to notify about does not exist.");

    /// <summary>A suspension notice needs the end date of the suspension, and the event did not carry one.</summary>
    public static readonly Error EffectiveUntilRequired =
        Error.Validation("notifications.effective_until_required", "A suspension notification needs the end date of the suspension.");

    // Template
    /// <summary>No active template of the type exists (NOT-02).</summary>
    public static readonly Error TemplateNotFound =
        Error.NotFound("notifications.template_not_found", "There is no active template for this notification type.");

    /// <summary>No template has this id (NOT-09).</summary>
    public static readonly Error TemplateDoesNotExist =
        Error.NotFound("notifications.template_does_not_exist", "The notification template does not exist.");

    /// <summary>A placeholder is not written as {{Name}} (NOT-09); the raw braces would reach the employee.</summary>
    public static readonly Error TemplateMalformedPlaceholder =
        Error.Validation("notifications.template_malformed_placeholder",
            "The template has a malformed placeholder; use {{Name}} with letters, digits or _ only.");

    /// <summary>
    /// The template uses a placeholder that has no value for its type (NOT-09): sending would fail, so it is refused when
    /// the template is saved. The names are in the extensions.
    /// </summary>
    public static readonly Error TemplateUnknownPlaceholder =
        Error.Validation("notifications.template_unknown_placeholder",
            "The template uses a placeholder that is not available for this notification type.");

    /// <summary>The active template cannot be deleted (NOT-09): the type would be left without one and could not be sent.</summary>
    public static readonly Error TemplateActiveCannotBeDeleted =
        Error.BusinessRule("notifications.template_active_cannot_be_deleted",
            "The active template cannot be deleted. Activate another template of this type first.");

    public static readonly Error TemplateSubjectRequired =
        Error.Validation("notifications.template_subject_required", "A template subject is required.");

    public static readonly Error TemplateSubjectTooLong =
        Error.Validation("notifications.template_subject_too_long", "The template subject must be at most 300 characters.");

    public static readonly Error TemplateBodyRequired =
        Error.Validation("notifications.template_body_required", "A template body is required.");
}
