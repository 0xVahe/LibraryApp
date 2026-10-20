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
    /// issue moment, both included. An Active loan due before <paramref name="overdueIfDueBeforeUtc"/> is Overdue in the
    /// result and in the status filter (the daily job has not marked it yet): the status is the effective one.
    /// </summary>
    Task<PagedResult<LoanHistoryItemDto>> PageAsync(
        Guid? staffMemberId,
        IReadOnlyCollection<Guid>? bookCopyIds,
        LoanStatus? status,
        DateTime? issuedFromUtc,
        DateTime? issuedToUtc,
        DateTime overdueIfDueBeforeUtc,
        PageRequest page,
        CancellationToken ct = default);
}
