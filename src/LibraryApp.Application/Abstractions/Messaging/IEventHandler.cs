using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Abstractions.Messaging;

/// <summary>
/// Handles one event type. Put the implementation in <c>Application/&lt;Module&gt;/EventHandlers/</c>;
/// it is found and registered automatically (no manual DI line).
/// <para>
/// A handler MUST be idempotent: the same event can be delivered more than once (outbox guarantees "at least once").
/// Check <see cref="IDomainEvent.EventId"/> (and the business key, for example NOT-06) before creating anything.
/// </para>
/// </summary>
public interface IEventHandler<in TEvent>
    where TEvent : class, IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}
