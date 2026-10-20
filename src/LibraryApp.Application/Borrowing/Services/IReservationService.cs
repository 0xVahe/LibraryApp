using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>Reservation operations of the Borrowing module.</summary>
public interface IReservationService
{
    /// <summary>
    /// Places the staff member at the end of the book's queue (BOR-07, BR-BOR-2, BR-BOR-3). A Staff Member reserves only for
    /// themselves; a Librarian or Library Administrator names the staff member (NFR-03).
    /// </summary>
    Task<ReservationDto> CreateAsync(CreateReservationRequest request, CancellationToken ct = default);

    /// <summary>One reservation; a Staff Member sees only their own (others are reported as not found).</summary>
    Task<ReservationDto> GetAsync(Guid reservationId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a Waiting or ReadyForPickup reservation (BOR-12); a Staff Member may cancel only their own, another one is
    /// reported as not found (NFR-03). A held copy goes to the next waiting member or becomes Available.
    /// </summary>
    Task<ReservationDto> CancelAsync(Guid reservationId, CancellationToken ct = default);
}
