using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Messaging;

/// <summary>Finds the <see cref="IEventHandler{TEvent}"/> implementations of an event and calls one of them.</summary>
public sealed class EventHandlerInvoker
{
    private static readonly ConcurrentDictionary<Type, (Type Contract, MethodInfo Handle)> Cache = new();

    /// <summary>Stable names (full class names) of the handlers registered for the event type.</summary>
    public IReadOnlyList<string> HandlerNames(Type eventType, IServiceProvider services) =>
        services.GetServices(Contract(eventType).Contract).Select(h => NameOf(h!)).Distinct().ToList();

    /// <summary>Runs the handler with the given name, resolved from <paramref name="services"/> (the caller's scope).</summary>
    public async Task InvokeAsync(object domainEvent, string handlerName, IServiceProvider services, CancellationToken ct)
    {
        var (contract, handle) = Contract(domainEvent.GetType());
        var handler = services.GetServices(contract).FirstOrDefault(h => NameOf(h!) == handlerName)
            ?? throw new InvalidOperationException($"Handler {handlerName} is not registered for {domainEvent.GetType().Name}.");

        try
        {
            await (Task)handle.Invoke(handler, [domainEvent, ct])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    public static string NameOf(object handler) => handler.GetType().FullName ?? handler.GetType().Name;

    private static (Type Contract, MethodInfo Handle) Contract(Type eventType) =>
        Cache.GetOrAdd(eventType, static type =>
        {
            var contract = typeof(IEventHandler<>).MakeGenericType(type);
            return (contract, contract.GetMethod(nameof(IEventHandler<IDomainEvent>.HandleAsync))!);
        });
}
