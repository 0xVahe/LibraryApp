namespace LibraryApp.Application.Notifications.Options;

/// <summary>Settings section "Notifications" (SD-21), checked at start.</summary>
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public DeliveryOptions Delivery { get; set; } = new();
}

/// <summary>Sending Pending emails in the background (NOT-03).</summary>
public sealed class DeliveryOptions
{
    public TimeSpan PollInterval { get; set; }

    public int BatchSize { get; set; }

    /// <summary>How long a claimed email is reserved for one instance; longer than sending one batch.</summary>
    public TimeSpan Lease { get; set; }

    /// <summary>Wait before each retry of a Failed email (NOT-08); the number of values is the number of retries.</summary>
    public TimeSpan[] RetryDelays { get; set; } = [];
}
