using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Abstractions.Messaging;

/// <summary>
/// The shared in-process event bus (INT-06, NOT-01). Provider: Integration Owner. Consumers: every module.
/// <para>
/// Where to call it depends on the delivery mode (SD-07, setting <c>Messaging:Delivery</c>):
/// <b>Outbox</b> (default) - call it INSIDE <see cref="Common.IUnitOfWork"/>: the event is saved together with the business change.
/// <b>Direct</b> - the event is delivered immediately, so call it AFTER the transaction has committed.
/// Publisher code is written for Outbox; switching to Direct is a one-line configuration change for the platform,
/// but publishers must then publish after commit.
/// </para>
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent;
}
