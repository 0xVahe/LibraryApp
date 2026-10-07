using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Common;
using Microsoft.Extensions.Logging;

namespace LibraryApp.Infrastructure.Messaging;

/// <summary>
/// Direct delivery (fallback, setting <c>Messaging:Delivery = Direct</c>): every handler runs at once in the
/// publisher's scope and transaction. A failing handler is logged and does not stop the others. An event is lost if
/// the process stops between the change and the handlers.
/// </summary>
public sealed class InProcessEventBus(IServiceProvider services, EventHandlerInvoker invoker, ILogger<InProcessEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent
    {
        foreach (var handler in invoker.HandlerNames(domainEvent.GetType(), services))
        {
            try
            {
                await invoker.InvokeAsync(domainEvent, handler, services, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Event handler {Handler} failed for {EventType} {EventId}", handler, domainEvent.GetType().Name, domainEvent.EventId);
            }
        }
    }
}
