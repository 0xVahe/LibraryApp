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
}
