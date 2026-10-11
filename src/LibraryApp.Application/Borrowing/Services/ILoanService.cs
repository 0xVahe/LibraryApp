using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// The loan operations of the Borrowing module. Controllers call this interface and contain no logic themselves.
/// </summary>
public interface ILoanService
{
    /// <summary>
    /// Issues a copy to an employee (BOR-01, BOR-02).
    /// </summary>
    /// <param name="request">The employee and the barcode of the copy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new loan.</returns>
    /// <exception cref="LibraryApp.Domain.Common.DomainException">
    /// Thrown when the employee may not borrow, the loan limit is reached, the barcode is unknown,
    /// the copy is not available, or Catalog rejects the status change.
    /// </exception>
    Task<LoanDto> IssueAsync(IssueLoanRequest request, CancellationToken ct = default);

    /// <summary>One loan. Throws <c>borrowing.loan_not_found</c> when there is none.</summary>
    Task<LoanDto> GetAsync(Guid loanId, CancellationToken ct = default);
}