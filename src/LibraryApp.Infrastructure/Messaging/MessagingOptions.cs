namespace LibraryApp.Infrastructure.Messaging;

public enum EventDeliveryMode
{
    /// <summary>
    /// Transactional outbox (SD-07): the event is stored with the business change in the same transaction and
    /// delivered by <see cref="OutboxDispatcher"/> at least once; each handler runs once per event (inbox). Default.
    /// </summary>
    Outbox = 1,

    /// <summary>Handlers run immediately in the publisher's scope; an event is lost if the process stops. Fallback only.</summary>
    Direct = 2,
}

/// <summary>Configuration section <c>Messaging</c>. Defaults suit one or a few API instances and thousands of events a day.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public EventDeliveryMode Delivery { get; set; } = EventDeliveryMode.Outbox;

    /// <summary>Runs the background dispatcher in this process (false in tests that deliver by hand).</summary>
    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>How long the dispatcher sleeps when nothing is due. A round that delivered something continues at once.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How many due messages one round claims.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>A claimed message is invisible to other instances this long; after a crash it becomes due again.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>After this many failed rounds a message is no longer retried (dead letter: logged, reported by the health check).</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Delay before the first retry; it doubles with every failure up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Processed messages and handler records older than this are deleted. Dead letters are kept.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Delay before the next attempt after <paramref name="failedAttempts"/> failures.</summary>
    public TimeSpan RetryDelay(int failedAttempts)
    {
        var factor = Math.Pow(2, Math.Max(0, failedAttempts - 1));
        return TimeSpan.FromTicks((long)Math.Min(RetryBaseDelay.Ticks * factor, MaxRetryDelay.Ticks));
    }
}
