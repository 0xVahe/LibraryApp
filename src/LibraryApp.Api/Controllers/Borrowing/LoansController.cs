using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Borrowing;

[ApiController]
[Route("api/loans")]
[Authorize(Roles = Librarians)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LoansController(
    IPenaltyService _penaltyService,
    ILoanService loans,
    ILoanReturnService loanReturns,
    ILoanQueryService loanQueryService) : ControllerBase
{
    private const string Librarians =
        StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>
    /// Issues a copy, found by its barcode, to a staff member (BOR-01, BOR-02).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<LoanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanDto>> Issue(
        IssueLoanRequest request,
        CancellationToken ct)
    {
        LoanDto loan = await loans.IssueAsync(request, ct);

        return CreatedAtAction(
            nameof(Get),
            new { id = loan.LoanId },
            loan);
    }

    /// <summary>
    /// Gets one loan.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(
        Guid id,
        CancellationToken ct) =>
        Ok(await loans.GetAsync(id, ct));

    /// <summary>
    /// Renews an existing loan (G3-03).
    /// </summary>
    [HttpPost("{id:guid}/renew")]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanDto>> Renew(
        Guid id,
        CancellationToken ct)
    {
        LoanDto loan = await loans.RenewAsync(id, ct);

        return Ok(loan);
    }

    /// <summary>
    /// Returns a loan and applies late or damaged-copy penalties
    /// (BOR-03, BOR-05, BOR-09).
    /// </summary>
    /// <remarks>
    /// A damaged copy goes UnderRepair.
    /// An undamaged copy is offered to the reservation queue;
    /// otherwise, it becomes Available.
    /// All changes are committed in one transaction.
    /// </remarks>
    [HttpPost("{id:guid}/return")]
    [ProducesResponseType<ReturnLoanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReturnLoanResponse>> Return(
        Guid id,
        [FromBody] ReturnLoanRequest request,
        CancellationToken ct)
    {
        ReturnLoanResponse result =
            await loanReturns.ReturnAsync(id, request, ct);

        return Ok(result);
    }

    /// <summary>
    /// Waives a specific borrowing penalty record by ID.
    /// </summary>
    /// <param name="penaltyId">The unique identifier of the penalty.</param>
    /// <param name="request">The waiver request containing the mandatory reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Penalty successfully waived.</response>
    /// <response code="400">Invalid waiver reason or penalty not active.</response>
    /// <response code="404">Penalty record not found.</response>
    [HttpPost("penalties/{penaltyId:guid}/waive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WaivePenalty(
        Guid penaltyId,
        [FromBody] WaivePenaltyRequest request,
        CancellationToken cancellationToken)
    {
        await _penaltyService.WaivePenaltyAsync(penaltyId, request.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Retrieves paginated loan history with optional filters for staff members or book copies.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType<PagedResult<LoanDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LoanDto>>> GetLoanHistory(
        [FromQuery] Guid? staffMemberId,
        [FromQuery] Guid? bookCopyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await loanQueryService.GetLoanHistoryAsync(
            staffMemberId,
            bookCopyId,
            page,
            pageSize,
            ct);

        return Ok(result);
    }
}