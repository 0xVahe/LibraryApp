namespace LibraryApp.Domain.Notifications;

/// <summary>
/// Delivery state of a notification (NOT-04). Explicit values starting at 1 (SD-11).
/// </summary>
public enum NotificationStatus
{
    /// <summary>Created, not delivered yet (G4-09: the row exists before sending).</summary>
    Pending = 1,

    /// <summary>Delivered.</summary>
    Sent = 2,

    /// <summary>Delivery failed; retried while NextAttemptAtUtc is set (NOT-08).</summary>
    Failed = 3,
}
