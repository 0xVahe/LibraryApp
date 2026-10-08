using System.Reflection;
using LibraryApp.Domain.Common;

namespace LibraryApp.Infrastructure.Messaging;

/// <summary>Maps the short name stored in the outbox ("LoanIssued") to the event record type and back.</summary>
public sealed class EventTypeRegistry
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);

    public EventTypeRegistry(IEnumerable<Assembly> assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes()))
        {
            if (!type.IsClass || type.IsAbstract || !typeof(IDomainEvent).IsAssignableFrom(type))
                continue;

            if (!_byName.TryAdd(type.Name, type) && _byName[type.Name] != type)
                throw new InvalidOperationException(
                    $"Two event types are named '{type.Name}' ({_byName[type.Name].FullName} and {type.FullName}). Event names must be unique.");
        }
    }

    public string NameOf(Type eventType) =>
        _byName.TryGetValue(eventType.Name, out var known) && known == eventType
            ? eventType.Name
            : throw new InvalidOperationException($"{eventType.FullName} is not a registered event type. Put event records in Domain/Events.");

    public Type Resolve(string name) =>
        _byName.TryGetValue(name, out var type)
            ? type
            : throw new InvalidOperationException($"Unknown event type '{name}'.");
}
