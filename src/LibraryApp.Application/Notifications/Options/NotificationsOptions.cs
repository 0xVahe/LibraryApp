namespace LibraryApp.Application.Notifications.Options;

/// <summary>Settings section "Notifications" (SD-21), checked at start.</summary>
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public DeliveryOptions Delivery { get; set; } = new();

    public RemindersOptions Reminders { get; set; } = new();
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

/// <summary>The 09:00 due-soon and overdue reminders (NOT-05); all counts are working days.</summary>
public sealed class RemindersOptions
{
    /// <summary>A loan gets its DueSoonReminder when it is due within this many working days.</summary>
    public int DueSoonWorkingDays { get; set; }

    /// <summary>An overdue loan gets an OverdueNotice every this many working days overdue.</summary>
    public int OverdueEveryWorkingDays { get; set; }

    /// <summary>The line manager is in CC once the loan is overdue for more than this many working days.</summary>
    public int LineManagerCcAfterWorkingDays { get; set; }
}
