using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Notifications;

/// <summary>
/// Editable text of one notification type with placeholders such as {{EmployeeName}}, {{BookTitle}},
/// {{DueDate}} (NOT-02). At most one active template per type.
/// </summary>
public sealed class NotificationTemplate : Entity
{
    public const int SubjectMaxLength = 300;   // the subject of the rendered notification

    public NotificationType Type { get; private set; }

    public string Subject { get; private set; } = null!;

    /// <summary>HTML body with placeholders; unlimited length.</summary>
    public string BodyTemplate { get; private set; } = null!;

    public bool IsActive { get; private set; }

    // For EF Core only.
    private NotificationTemplate()
    {
    }

    public NotificationTemplate(Guid id, NotificationType type, string subject, string bodyTemplate, bool isActive = true)
        : base(id)
    {
        Type = type;
        Update(subject, bodyTemplate);
        IsActive = isActive;
    }

    /// <summary>Changes the text (NOT-09).</summary>
    public void Update(string subject, string bodyTemplate)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new DomainException(NotificationErrors.TemplateSubjectRequired);

        if (subject.Trim().Length > SubjectMaxLength)
            throw new DomainException(NotificationErrors.TemplateSubjectTooLong);

        if (string.IsNullOrWhiteSpace(bodyTemplate))
            throw new DomainException(NotificationErrors.TemplateBodyRequired);

        Subject = subject.Trim();
        BodyTemplate = bodyTemplate;
    }

    /// <summary>
    /// Makes this template the active one of its type. The caller deactivates the previous one
    /// in the same transaction; the database refuses two active templates of one type.
    /// </summary>
    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
