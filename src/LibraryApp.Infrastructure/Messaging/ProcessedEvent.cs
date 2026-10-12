namespace LibraryApp.Infrastructure.Messaging;

/// <summary>
/// Inbox record: handler <see cref="Handler"/> has processed event <see cref="EventId"/>. Written in the same
/// transaction as the handler's own changes, so a redelivered event never runs a handler twice (SD-07, G4-09).
/// Deleted together with old outbox messages.
/// </summary>
public sealed class ProcessedEvent
{
    public const int HandlerMaxLength = 400;

    private ProcessedEvent() { }

    public Guid EventId { get; private set; }

    /// <summary>Full type name of the handler class. Renaming a handler class makes it a new handler.</summary>
    public string Handler { get; private set; } = "";

    public DateTime ProcessedAtUtc { get; private set; }

    public static ProcessedEvent Create(Guid eventId, string handler, DateTime processedAtUtc) =>
        new() { EventId = eventId, Handler = handler, ProcessedAtUtc = processedAtUtc };
}
