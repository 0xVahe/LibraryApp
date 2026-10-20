using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>What the API returns about a reservation (BOR-07). The Reservation entity is never returned directly.</summary>
/// <param name="ReservationId">The reservation.</param>
/// <param name="BookId">The reserved book; any copy of it serves the reservation.</param>
/// <param name="StaffMemberId">The staff member who waits for the book.</param>
/// <param name="CreatedAtUtc">When the reservation was placed (UTC); it fixes the place in the FIFO queue.</param>
/// <param name="Status">Waiting, ReadyForPickup, Fulfilled, Cancelled or Expired.</param>
/// <param name="QueuePosition">1..n while Waiting (1 = next to get a copy), 0 otherwise.</param>
/// <param name="CopyId">The copy held at the library desk while ReadyForPickup; kept afterwards for history, otherwise null.</param>
/// <param name="ReadyUntilUtc">The end of the pickup period while ReadyForPickup (UTC), otherwise null.</param>
public sealed record ReservationDto(
    Guid ReservationId,
    Guid BookId,
    Guid StaffMemberId,
    DateTime CreatedAtUtc,
    ReservationStatus Status,
    int QueuePosition,
    Guid? CopyId,
    DateTime? ReadyUntilUtc);
