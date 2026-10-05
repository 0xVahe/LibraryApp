using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Abstractions.Messaging;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class, IDomainEvent;
}
