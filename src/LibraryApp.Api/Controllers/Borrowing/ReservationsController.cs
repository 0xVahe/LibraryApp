using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Borrowing;

/// <summary>
/// Reservations (BOR-07, BOR-12). Librarian and Library Administrator reserve for a named staff member and cancel any
/// reservation; a Staff Member reserves, reads and cancels only their own (NFR-03).
/// </summary>
[ApiController]
[Route("api/reservations")]
[Authorize(Roles = Callers)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ReservationsController(IReservationService reservations) : ControllerBase
{
    private const string Callers = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian + "," + StaffRoles.StaffMember;

    /// <summary>Places a staff member at the end of the waiting queue of a book (BOR-07).</summary>
    /// <remarks>
    /// Only when no copy of the book is Available (BR-BOR-3), never for a book the staff member holds, at most
    /// <c>Borrowing:Reservations:MaxActive</c> active reservations (BR-BOR-2, 3 by default); Restricted and Suspended staff
    /// members are refused unless the settings allow it, Deactivated ones always (422). Already in the
    /// queue of this book: 409. A Staff Member leaves <c>staffMemberId</c> out or sends their own.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationDto>> Create(CreateReservationRequest request, CancellationToken ct)
    {
        ReservationDto reservation = await reservations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = reservation.ReservationId }, reservation);
    }

    /// <summary>One reservation; a Staff Member sees only their own.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await reservations.GetAsync(id, ct));

    /// <summary>Cancels a waiting or ready reservation (BOR-12); a Staff Member may cancel only their own.</summary>
    /// <remarks>
    /// Waiting: the reservation leaves the queue and the members behind it move up (positions stay 1..n).
    /// ReadyForPickup: the held copy goes to the first waiting reservation of the book, which becomes ReadyForPickup
    /// (<c>ReservationReady</c>), or becomes Available when nobody waits. Returns the cancelled reservation.
    /// Unknown id, or another staff member's reservation for a Staff Member: 404. Already Cancelled, Fulfilled or
    /// Expired: 422 <c>borrowing.reservation_cannot_be_cancelled</c>. A concurrent change of the same row: 409, retry.
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationDto>> Cancel(Guid id, CancellationToken ct) =>
        Ok(await reservations.CancelAsync(id, ct));
}
