using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Loan data for Catalog (CAT-08). Provider: Borrowing (Group 3). Read only, no transaction of its own.</summary>
public interface ILoanQueryService
{
    /// <summary>
    /// The earliest due date (UTC) among the open loans (Active or Overdue) of the copies of this book; null when no
    /// copy is on loan. An Overdue loan counts: its due date is in the past, but the copy is still out.
    /// </summary>
    Task<DateTime?> GetNextDueDateAsync(
        Guid bookId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a paginated list of historical loans filtered optionally by staff member or book copy.
    /// </summary>
    /// <param name="staffMemberId">Optional staff member identifier filter.</param>
    /// <param name="bookCopyId">Optional book copy identifier filter.</param>
    /// <param name="page">The page number starting from 1.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result containing loan data transfer objects.</returns>
    Task<PagedResult<LoanDto>> GetLoanHistoryAsync(
        Guid? staffMemberId,
        Guid? bookCopyId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}