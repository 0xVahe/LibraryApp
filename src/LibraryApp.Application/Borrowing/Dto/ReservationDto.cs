using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>What the API returns about a reservation. The Reservation entity is never returned directly.</summary>
public sealed record ReservationDto(
    Guid ReservationId,
    Guid BookId,
    Guid StaffMemberId,
    DateTime CreatedAtUtc,
    ReservationStatus Status,
    int QueuePosition,
    Guid? CopyId,
    DateTime? ReadyUntilUtc);
