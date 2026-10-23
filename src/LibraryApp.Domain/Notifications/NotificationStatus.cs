namespace LibraryApp.Domain.Notifications;

/// <summary>
/// Delivery state of a notification (NOT-04). Explicit values starting at 1, so 0 is never a valid value.
/// </summary>
public enum NotificationStatus
{
    /// <summary>Created, not delivered yet: the row exists before sending.</summary>
    Pending = 1,

    /// <summary>Delivered.</summary>
    Sent = 2,

    /// <summary>Delivery failed; retried while NextAttemptAtUtc is set (NOT-08).</summary>
    Failed = 3,
}
