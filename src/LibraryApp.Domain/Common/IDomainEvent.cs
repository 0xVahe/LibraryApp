namespace LibraryApp.Domain.Common;

/// <summary>
/// Marker for every event that travels through <c>IEventBus</c> (INT-07, SD-08).
/// Rules for implementing records (they live in <c>Domain/Events</c>):
/// carry only IDs and simple values, never whole entities; set <see cref="EventId"/> and
/// <see cref="OccurredAtUtc"/> in the publisher; after the contract freeze only add fields, never rename or remove.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredAtUtc { get; }
}
