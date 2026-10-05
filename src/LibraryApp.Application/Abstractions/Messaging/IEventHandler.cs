using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Abstractions.Messaging;

public interface IEventHandler<in TEvent>
    where TEvent : class, IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
