using System.Text.Json;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;

namespace LibraryApp.Infrastructure.Messaging;

/// <summary>
/// Outbox delivery (SD-07): the event is added to the same DbContext, so it is saved by the same SaveChanges and the
/// same transaction as the business change. If the transaction rolls back, the event never existed.
/// </summary>
public sealed class OutboxEventBus(LibraryDbContext db, EventTypeRegistry registry) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "An event must be published inside IUnitOfWork.ExecuteInTransactionAsync, so it is saved together with the change it describes.");

        if (domainEvent.EventId == Guid.Empty)
            throw new ArgumentException("EventId must be set by the publisher (IIdGenerator).", nameof(domainEvent));

        var type = domainEvent.GetType();
        var payload = JsonSerializer.Serialize(domainEvent, type);

        db.Set<OutboxMessage>().Add(OutboxMessage.Create(domainEvent.EventId, registry.NameOf(type), payload, domainEvent.OccurredAtUtc));
        return Task.CompletedTask;
    }
}
