namespace LibraryApp.Infrastructure.Messaging;

/// <summary>
/// One event waiting for delivery (transactional outbox, SD-07). Stored by <see cref="OutboxEventBus"/> in the same
/// transaction as the business change and delivered by <see cref="OutboxDispatcher"/>. The key is the event's
/// <c>EventId</c>, so one event is never stored twice. Processed rows are deleted after <see cref="MessagingOptions.Retention"/>.
/// </summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 200;
    public const int LastErrorMaxLength = 2000;

    private OutboxMessage() { }

    public Guid Id { get; private set; }

    /// <summary>Short name of the event record, for example <c>LoanIssued</c>.</summary>
    public string Type { get; private set; } = "";

    public string Payload { get; private set; } = "";

    public DateTime OccurredAtUtc { get; private set; }

    public DateTime? ProcessedAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    /// <summary>Set while one dispatcher instance delivers the message (lease).</summary>
    public DateTime? LockedUntilUtc { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(Guid eventId, string type, string payload, DateTime occurredAtUtc) =>
        new() { Id = eventId, Type = type, Payload = payload, OccurredAtUtc = occurredAtUtc };
}
