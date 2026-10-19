using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Borrowing;

/// <summary>Loans (BOR-01, BOR-02). Issuing and reading a loan: Librarian or Administrator (NFR-03).</summary>
[ApiController]
[Route("api/loans")]
[Authorize(Roles = Librarians)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LoansController(
    ILoanService loans,
    ILoanReturnService loanReturns,
    ILoanLostService lostLoans) : ControllerBase
{
    private const string Librarians = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>Issues a copy, found by its barcode, to a staff member (BOR-01, BOR-02).</summary>
    /// <remarks>
    /// The due date is the issue date plus the loan days of the staff member's category. A Reserved copy can be issued
    /// only to the staff member whose ReadyForPickup reservation holds it. Issuing an Available copy also closes that
    /// member's active reservation for the same book (setting Borrowing:Reservations:AnyCopyFulfils, BQ-BA-08): a ready
    /// one is fulfilled and its held copy offered to the next member, a waiting one is cancelled.
    /// Refused with 422 when the staff member may not borrow, the loan limit is reached ("Loan limit reached (3/3)")
    /// or the copy is unavailable to them; 404 when no copy has the barcode.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<LoanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanDto>> Issue(IssueLoanRequest request, CancellationToken ct)
    {
        LoanDto loan = await loans.IssueAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = loan.LoanId }, loan);
    }

    /// <summary>
    /// Declares a borrowed copy lost and applies lost points.
    /// </summary>
    /// <remarks>
    /// No request body is required.
    /// Active and Overdue loans are accepted by default.
    /// </remarks>
    [HttpPost("{id:guid}/lost")]
    [ProducesResponseType<DeclareLostResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DeclareLostResponse>> DeclareLost(
        Guid id,
        CancellationToken ct)
    {
        DeclareLostResponse result =
            await lostLoans.DeclareLostAsync(id, ct);

        return Ok(result);
    }
    
    /// <summary>One loan.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await loans.GetAsync(id, ct));
    
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
}
