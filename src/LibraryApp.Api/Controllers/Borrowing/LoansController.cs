using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Borrowing;

/// <summary>
/// Loans (BOR-01…BOR-04, BOR-09, BOR-10). Issuing, returning, declaring lost and reading one loan: Librarian or
/// Administrator. Renewing and the history: also a Staff Member, for their own loans only (NFR-03).
/// </summary>
[ApiController]
[Route("api/loans")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LoansController(
    ILoanService loans,
    ILoanReturnService loanReturns,
    ILoanLostService lostLoans,
    ILoanRenewalService renewals,
    ILoanHistoryService history) : ControllerBase
{
    private const string Librarians = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;
    private const string Callers = Librarians + "," + StaffRoles.StaffMember;

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
    [Authorize(Roles = Librarians)]
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
    /// No request body is required. The loan and the copy become Lost, and the lost points of the policy
    /// (<c>Borrowing:Penalties:LostPoints</c>, default 10) are applied; the staff status changes when the points cross
    /// a threshold. Active and Overdue loans are accepted by default; with
    /// <c>Borrowing:Loans:AllowLostBeforeDue</c> = false an Active loan that is not past the overdue rule is refused
    /// with 422. 404 when the loan does not exist; 422 when it is already returned or lost.
    /// </remarks>
    [HttpPost("{id:guid}/lost")]
    [Authorize(Roles = Librarians)]
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
    [Authorize(Roles = Librarians)]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await loans.GetAsync(id, ct));

    /// <summary>Loan history with filters and paging (BOR-10), newest first.</summary>
    /// <remarks>
    /// Filters: <c>staffMemberId</c>, <c>bookId</c> (every copy of the book, retired copies included), <c>status</c>
    /// (Active, Overdue, Returned, Lost). Page size 20 by default, at most 100. A Staff Member always gets their own
    /// loans only, whatever <c>staffMemberId</c> says (NFR-03).
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = Callers)]
    [ProducesResponseType<PagedResult<LoanHistoryItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<LoanHistoryItemDto>>> List(
        [FromQuery] Guid? staffMemberId,
        [FromQuery] Guid? bookId,
        [FromQuery] LoanStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await history.ListAsync(
            new LoanHistoryFilter(staffMemberId, bookId, status),
            new PageRequest(page, pageSize),
            ct));

    /// <summary>Renews a loan (BOR-04): the due date moves by the loan days of the staff member's category.</summary>
    /// <remarks>
    /// A Staff Member renews only their own loans (another person's loan: 404). Refused with 422, in this order
    /// (BR-BOR-1): the loan is overdue (<c>borrowing.loan_overdue_cannot_be_renewed</c>); the staff member is Restricted
    /// (<c>borrowing.restricted_renewal_forbidden</c>) or Suspended (<c>borrowing.suspended_renewal_forbidden</c>, unless
    /// <c>Borrowing:Loans:SuspendedMayRenew</c>); the renewals reached the category's maximum ("Maximum renewals
    /// reached", <c>borrowing.max_renewals_reached</c>); another staff member waits for the book
    /// (<c>borrowing.renewal_blocked_by_reservation</c>). A returned or lost loan: 422
    /// <c>borrowing.loan_cannot_be_renewed</c>. Publishes <c>LoanRenewed</c>. A concurrent change of the same loan: 409.
    /// </remarks>
    [HttpPost("{id:guid}/renew")]
    [Authorize(Roles = Callers)]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanDto>> Renew(Guid id, CancellationToken ct) =>
        Ok(await renewals.RenewAsync(id, ct));

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
    [Authorize(Roles = Librarians)]
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
