using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Events;

/// <summary>A reserved copy is waiting for pickup (BOR-07). Publisher: Borrowing. Consumers: Notifications (ReservationReady).</summary>
public sealed record ReservationReady(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ReservationId,
    Guid BookId,
    Guid StaffMemberId,
    DateTime ReadyUntilUtc) : IDomainEvent;
