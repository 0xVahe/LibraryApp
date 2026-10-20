using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Borrowing;

/// <summary>
/// Penalty records (BOR-08): finding a staff member's records and waiving one. Librarian or Library Administrator;
/// a Staff Member sees their own points through My library (MEM-08).
/// </summary>
[ApiController]
[Route("api/penalties")]
[Authorize(Roles = Librarians)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class PenaltiesController(IPenaltyAdministrationService penalties) : ControllerBase
{
    private const string Librarians = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>A staff member's penalty records, newest first, with paging (default 20, at most 100).</summary>
    /// <remarks><c>status</c> (Active, Expired, Waived) narrows the list; an unknown staff member gives an empty page.</remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<PenaltyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PenaltyDto>>> List(
        [FromQuery] Guid staffMemberId,
        [FromQuery] PenaltyRecordStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await penalties.ListAsync(staffMemberId, status, new PageRequest(page, pageSize), ct));

    /// <summary>Waives an active penalty record with a mandatory reason (BOR-08).</summary>
    /// <remarks>
    /// The staff member's status is recalculated at once: below <c>Borrowing:Penalties:RestrictedFromPoints</c> they
    /// become Active, with <c>StaffStatusChanged</c> only on a real change. No reason, or one longer than 500
    /// characters: 400. Unknown record: 404 <c>borrowing.penalty_not_found</c>. Expired or already waived: 422
    /// <c>borrowing.penalty_not_active</c>. A concurrent change of the same record: 409, retry.
    /// </remarks>
    [HttpPost("{id:guid}/waive")]
    [ProducesResponseType<PenaltyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PenaltyDto>> Waive(Guid id, WaivePenaltyRequest request, CancellationToken ct) =>
        Ok(await penalties.WaiveAsync(id, request, ct));
}
