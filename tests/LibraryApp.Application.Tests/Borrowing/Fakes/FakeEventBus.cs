using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

/// <summary>Keeps published events; like the outbox, they are dropped when the transaction rolls back.</summary>
public sealed class FakeEventBus : IEventBus
{
    private readonly List<IDomainEvent> _pending = new();
    private readonly List<IDomainEvent> _published = new();

    public IReadOnlyList<IDomainEvent> Published => _published;

    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent
    {
        ct.ThrowIfCancellationRequested();
        _pending.Add(domainEvent);
        return Task.CompletedTask;
    }

    public void Commit()
    {
        _published.AddRange(_pending);
        _pending.Clear();
    }

    public void Rollback() => _pending.Clear();
}
