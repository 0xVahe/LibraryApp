using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Notifications;

/// <summary>
/// One message to one staff member on one channel (G4-05). Created as Pending before sending (G4-09),
/// so a failed send leaves a row to retry (NOT-08).
/// </summary>
public sealed class Notification : Entity
{
    public const int SubjectMaxLength = 300;   // G4-07
    public const int LastErrorMaxLength = 2000;

    /// <summary>The staff member the notification is for.</summary>
    public Guid StaffMemberId { get; private set; }

    /// <summary>The loan it is about, or null (Welcome, AccessRestricted, ...). Used for NOT-06.</summary>
    public Guid? LoanId { get; private set; }

    /// <summary>The event that caused it, or null for daily-job reminders. A repeated event is ignored (G4-09).</summary>
    public Guid? EventId { get; private set; }

    public NotificationType Type { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>Corporate email, stored when created so a retry goes to the same person (G4-06).</summary>
    public string RecipientEmail { get; private set; } = null!;

    /// <summary>Line manager in CC, stored when created (G4-06), or null.</summary>
    public string? CcEmail { get; private set; }

    public string Subject { get; private set; } = null!;

    /// <summary>Rendered HTML body; unlimited length (G4-07).</summary>
    public string Body { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Yerevan calendar date of <see cref="CreatedAtUtc"/> (from <c>IClock.ToLocalDate</c>).
    /// Part of the unique rule "same type for the same loan at most once a day" (NOT-06, G4-02).
    /// </summary>
    public DateOnly LocalDate { get; private set; }

    public DateTime? SentAtUtc { get; private set; }

    /// <summary>When the staff member marked it as read; null = unread (NOT-04, G4-01).</summary>
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>Number of failed send attempts (NOT-08, G4-03).</summary>
    public int Attempts { get; private set; }

    /// <summary>When the next retry is due; null = no more retries (NOT-08, G4-03).</summary>
    public DateTime? NextAttemptAtUtc { get; private set; }

    public string? LastError { get; private set; }

    // For EF Core only.
    private Notification()
    {
    }

    private Notification(Guid id) : base(id)
    {
    }

    /// <summary>Creates a pending notification. The id comes from IIdGenerator, the times from IClock.</summary>
    public static Notification Create(
        Guid id,
        Guid staffMemberId,
        NotificationType type,
        NotificationChannel channel,
        string recipientEmail,
        string? ccEmail,
        string subject,
        string body,
        DateTime createdAtUtc,
        DateOnly localDate,
        Guid? loanId = null,
        Guid? eventId = null)
    {
        if (staffMemberId == Guid.Empty || loanId == Guid.Empty || eventId == Guid.Empty)
            throw new DomainException(NotificationErrors.InvalidReferenceId);

        if (string.IsNullOrWhiteSpace(recipientEmail))
            throw new DomainException(NotificationErrors.RecipientRequired);

        if (string.IsNullOrWhiteSpace(subject))
            throw new DomainException(NotificationErrors.SubjectRequired);

        if (subject.Trim().Length > SubjectMaxLength)
            throw new DomainException(NotificationErrors.SubjectTooLong);

        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException(NotificationErrors.BodyRequired);

        EnsureUtc(createdAtUtc);

        return new Notification(id)
        {
            StaffMemberId = staffMemberId,
            LoanId = loanId,
            EventId = eventId,
            Type = type,
            Channel = channel,
            Status = NotificationStatus.Pending,
            RecipientEmail = recipientEmail.Trim(),
            CcEmail = string.IsNullOrWhiteSpace(ccEmail) ? null : ccEmail.Trim(),
            Subject = subject.Trim(),
            Body = body,
            CreatedAtUtc = createdAtUtc,
            LocalDate = localDate,
            Attempts = 0,
        };
    }

    /// <summary>Records a successful delivery.</summary>
    public void MarkSent(DateTime sentAtUtc)
    {
        EnsureUtc(sentAtUtc);

        if (Status == NotificationStatus.Sent)
            throw new DomainException(NotificationErrors.AlreadySent);

        Status = NotificationStatus.Sent;
        SentAtUtc = sentAtUtc;
        NextAttemptAtUtc = null;
        LastError = null;
    }

    /// <summary>
    /// Records a failed delivery. The caller decides the next retry time (5, 15, 60 minutes - NOT-08)
    /// and passes null when no retry is left.
    /// </summary>
    public void MarkFailed(string error, DateTime? nextAttemptAtUtc)
    {
        if (Status == NotificationStatus.Sent)
            throw new DomainException(NotificationErrors.AlreadySent);

        if (nextAttemptAtUtc is { } next)
            EnsureUtc(next);

        Attempts = checked(Attempts + 1);
        Status = NotificationStatus.Failed;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = Truncate(error, LastErrorMaxLength);
    }

    /// <summary>Marks an in-app notification as read (NOT-04). Reading twice keeps the first time.</summary>
    public void MarkRead(DateTime readAtUtc)
    {
        EnsureUtc(readAtUtc);

        if (Channel != NotificationChannel.InApp)
            throw new DomainException(NotificationErrors.OnlyInAppCanBeRead);

        ReadAtUtc ??= readAtUtc;
    }

    private static void EnsureUtc(DateTime timestamp)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
            throw new DomainException(NotificationErrors.TimestampMustBeUtc);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
