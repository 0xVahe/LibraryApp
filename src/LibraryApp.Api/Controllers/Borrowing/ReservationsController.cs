using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Borrowing;

/// <summary>
/// Reservations (BOR-07). Librarian and Library Administrator reserve for a named staff member; a Staff Member reserves
/// and reads only their own (NFR-03).
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
    /// Only when no copy of the book is Available (BR-BOR-3), never for a book the staff member holds, at most 3 active
    /// reservations (BR-BOR-2); Restricted, Suspended and Deactivated staff members are refused (422). Already in the
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
}
