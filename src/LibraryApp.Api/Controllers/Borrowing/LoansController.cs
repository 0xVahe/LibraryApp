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
    /// Librarian or Library Administrator. The barcode is matched as Catalog does (case and surrounding spaces ignored).
    /// The due date is the issue date plus the loan days of the staff member's category (Restricted: one loan at most,
    /// BR-MEM-3). A Reserved copy is issued only to the staff member whose ReadyForPickup reservation holds it, and that
    /// reservation becomes Fulfilled. With <c>Borrowing:Reservations:AnyCopyFulfils</c> (default true) issuing an Available
    /// copy also closes the member's active reservation of the same book: a ready one is fulfilled and its held copy goes
    /// to the next member in the queue, a waiting one is cancelled. One transaction (BR-BOR-4); publishes
    /// <c>LoanIssued</c>.
    /// <para>
    /// 400: no staff member id or no barcode. 404: no copy has the barcode (<c>borrowing.copy_not_found</c>), unknown staff
    /// member (<c>staff.not_found</c>). 422: the staff member may not borrow (Staff's reason: Suspended or Deactivated), the
    /// loan limit is reached
    /// (<c>staff.loan_limit_reached</c>, "Loan limit reached (3/3)"), the copy is not available to them
    /// (<c>borrowing.copy_not_available</c>). 409: the copy was changed at the same moment, retry.
    /// </para>
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

    /// <summary>Declares the copy of an open loan lost and applies the lost points (BOR-09).</summary>
    /// <remarks>
    /// Librarian or Library Administrator; no request body. The loan and the copy become Lost (the copy never returns to
    /// circulation) and the lost points of the penalty-point policy are added (<c>Borrowing:Penalties:LostPoints</c>,
    /// default 10); the staff status is recalculated (Restricted from 5, Suspended from 10 active points by default). One
    /// transaction (BR-BOR-4); publishes <c>PenaltyApplied</c> and, on a real change, <c>StaffStatusChanged</c>. Active and
    /// Overdue loans are accepted; with <c>Borrowing:Loans:AllowLostBeforeDue</c> = false an Active loan that is not past
    /// the overdue rule is refused.
    /// <para>
    /// 404 <c>borrowing.loan_not_found</c>. 422 <c>borrowing.loan_already_closed</c> (already returned or lost),
    /// <c>borrowing.loan_not_past_due</c>. 409: the loan or the copy was changed at the same moment, retry.
    /// </para>
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

    /// <summary>One loan, by id.</summary>
    /// <remarks>
    /// Librarian or Library Administrator. The stored status: Active, Overdue (marked by the daily job), Returned or Lost.
    /// 404 <c>borrowing.loan_not_found</c>.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = Librarians)]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await loans.GetAsync(id, ct));

    /// <summary>Loan history with filters and paging (BOR-10), newest issue first.</summary>
    /// <remarks>
    /// Every filter is optional: <c>staffMemberId</c>; <c>bookId</c> (every copy of the book, retired copies included);
    /// <c>status</c> (Active, Overdue, Returned, Lost — the effective status: an Active loan past the overdue rule is
    /// Overdue before the daily job marks it, in the filter and in the result); <c>issuedFrom</c> and <c>issuedTo</c>
    /// (local Asia/Yerevan dates, yyyy-MM-dd, both days included). Page size 20 by default, at most 100; a page below 1 is
    /// page 1. A Staff Member always gets their own loans only, whatever <c>staffMemberId</c> says (NFR-03).
    /// <para>
    /// 400: an unknown status or date, or a period that ends before it starts (<c>borrowing.history_period_invalid</c>).
    /// </para>
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = Callers)]
    [ProducesResponseType<PagedResult<LoanHistoryItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<LoanHistoryItemDto>>> List(
        [FromQuery] Guid? staffMemberId,
        [FromQuery] Guid? bookId,
        [FromQuery] LoanStatus? status,
        [FromQuery] DateOnly? issuedFrom,
        [FromQuery] DateOnly? issuedTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await history.ListAsync(
            new LoanHistoryFilter(staffMemberId, bookId, status, issuedFrom, issuedTo),
            new PageRequest(page, pageSize),
            ct));

    /// <summary>Renews a loan (BOR-04): the due date moves by the loan days of the staff member's category.</summary>
    /// <remarks>
    /// Any signed-in role; a Staff Member renews only their own loans. <c>RenewalCount</c> goes up by one. One
    /// transaction (BR-BOR-4); publishes <c>LoanRenewed</c>.
    /// <para>
    /// 404 <c>borrowing.loan_not_found</c>, also for another person's loan asked by a Staff Member (NFR-03). 422, checked in
    /// this order (BR-BOR-1): the loan is overdue (<c>borrowing.loan_overdue_cannot_be_renewed</c>); the staff member is
    /// Restricted (<c>borrowing.restricted_renewal_forbidden</c>) or Suspended (<c>borrowing.suspended_renewal_forbidden</c>,
    /// unless <c>Borrowing:Loans:SuspendedMayRenew</c>); the renewals reached the category's maximum
    /// (<c>borrowing.max_renewals_reached</c>, "Maximum renewals reached"); another staff member waits for the book
    /// (<c>borrowing.renewal_blocked_by_reservation</c>). A returned or lost loan: 422 <c>borrowing.loan_cannot_be_renewed</c>.
    /// 409: the loan was changed at the same moment, retry.
    /// </para>
    /// </remarks>
    [HttpPost("{id:guid}/renew")]
    [Authorize(Roles = Callers)]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanDto>> Renew(Guid id, CancellationToken ct) =>
        Ok(await renewals.RenewAsync(id, ct));

    /// <summary>Takes a copy back: closes the loan and applies the late and damaged points (BOR-03, BOR-05, BOR-09).</summary>
    /// <remarks>
    /// Librarian or Library Administrator. The loan becomes Returned with <c>ReturnedAtUtc</c>. Late points follow the
    /// penalty-point policy: 1 point per overdue working day (Saturdays, Sundays and the configured Armenian public
    /// holidays excluded, BR-BOR-5), at most 10 per loan; <c>Damaged</c> = true adds the damaged points (default 5) and
    /// sends the copy to repair (UnderRepair). An undamaged copy goes to the first Waiting reservation of the book, which
    /// becomes ReadyForPickup (<c>ReservationReady</c>), or becomes Available. The staff status is recalculated. One
    /// transaction (BR-BOR-4); publishes <c>LoanReturned</c>, <c>PenaltyApplied</c> for points and, on a real change,
    /// <c>StaffStatusChanged</c>. The response gives the overdue working days and the points applied.
    /// <para>
    /// 400: a note longer than 500 characters. 404 <c>borrowing.loan_not_found</c>. 422
    /// <c>borrowing.loan_already_closed</c> (already returned or lost). 409: the loan or the copy was changed at the same
    /// moment, retry.
    /// </para>
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
