using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>Read-only loan history (BOR-10): no tracking, no transaction.</summary>
public interface ILoanHistoryQuery
{
    /// <summary>
    /// One page of loans, newest issue first. <paramref name="bookCopyIds"/>, when given, limits the loans to those copies
    /// (an empty list gives an empty page). <paramref name="issuedFromUtc"/> and <paramref name="issuedToUtc"/> bound the
    /// issue moment, both included.
    /// </summary>
    Task<PagedResult<LoanHistoryItemDto>> PageAsync(
        Guid? staffMemberId,
        IReadOnlyCollection<Guid>? bookCopyIds,
        LoanStatus? status,
        DateTime? issuedFromUtc,
        DateTime? issuedToUtc,
        PageRequest page,
        CancellationToken ct = default);
}
