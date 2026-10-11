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
public sealed class LoansController(ILoanService loans) : ControllerBase
{
    private const string Librarians = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>Issues a copy, found by its barcode, to a staff member (BOR-01, BOR-02).</summary>
    /// <remarks>
    /// The due date is the issue date plus the loan days of the staff member's category. Refused with 422 when the
    /// staff member may not borrow, the loan limit is reached ("Loan limit reached (3/3).") or the copy is not
    /// Available; 404 when no copy has the barcode.
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

    /// <summary>One loan.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<LoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await loans.GetAsync(id, ct));
}
