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
    /// A Librarian or Library Administrator names the staff member; a Staff Member leaves <c>staffMemberId</c> out or
    /// sends their own. The reservation is Waiting with its place in the FIFO queue; when a copy comes back it becomes
    /// ReadyForPickup and is held for the pickup working days (3 by default).
    /// <para>
    /// 400: no book id, or a Librarian without <c>staffMemberId</c>. 403: a Staff Member names someone else. 404: unknown
    /// book (<c>catalog.book_not_found</c>) or staff member. 409 <c>borrowing.duplicate_active_reservation</c>: already in
    /// the queue of this book. 422: a copy is Available (<c>borrowing.reservation_copy_available</c>, BR-BOR-3); the staff
    /// member holds the book (<c>borrowing.book_already_on_loan</c>, BR-BOR-2); the limit of active reservations is
    /// reached (<c>borrowing.reservation_limit_reached</c>, <c>Borrowing:Reservations:MaxActive</c>, 3 by default,
    /// BR-BOR-2); Restricted (<c>borrowing.restricted_reservation_forbidden</c>, BR-MEM-3) or Suspended (Staff's reason)
    /// unless the settings allow it; Deactivated always (Staff's reason).
    /// </para>
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

    /// <summary>One reservation, by id.</summary>
    /// <remarks>
    /// Any signed-in role; a Staff Member sees only their own. <c>QueuePosition</c> is 1..n while Waiting and 0 otherwise;
    /// <c>CopyId</c> and <c>ReadyUntilUtc</c> are set while ReadyForPickup. 404 <c>borrowing.reservation_not_found</c>,
    /// also for another person's reservation asked by a Staff Member (NFR-03).
    /// </remarks>
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
    /// <para>
    /// 404 <c>borrowing.reservation_not_found</c>: unknown id, or another person's reservation for a Staff Member. 422
    /// <c>borrowing.reservation_cannot_be_cancelled</c>: already Cancelled, Fulfilled or Expired. 409: the reservation was
    /// changed at the same moment, retry.
    /// </para>
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationDto>> Cancel(Guid id, CancellationToken ct) =>
        Ok(await reservations.CancelAsync(id, ct));
}
