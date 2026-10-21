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
    /// <remarks>
    /// Librarian or Library Administrator. <c>status</c> (Active, Expired, Waived) narrows the list; an unknown staff
    /// member gives an empty page. Each record has its points, reason, expiry (90 days by default) and, when waived, the
    /// reason of the waiver. 400: an unknown status.
    /// </remarks>
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
    /// Librarian or Library Administrator. The record becomes Waived with the reason and no longer counts. The staff
    /// member's status is recalculated in the same transaction: below <c>Borrowing:Penalties:RestrictedFromPoints</c>
    /// they become Active, with <c>StaffStatusChanged</c> only on a real change.
    /// <para>
    /// 400: no reason, or one longer than 500 characters. 404 <c>borrowing.penalty_not_found</c>. 422
    /// <c>borrowing.penalty_not_active</c>: expired or already waived. 409: the record was changed at the same moment,
    /// retry.
    /// </para>
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
