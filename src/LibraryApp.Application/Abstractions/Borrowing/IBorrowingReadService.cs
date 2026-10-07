using LibraryApp.Domain.Borrowing;
using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Abstractions.Borrowing;

public interface IBorrowingReadService
{
    // BR-MEM-4: check open loans before deactivation.
    Task<IReadOnlyList<OpenLoanInfo>> GetOpenLoansAsync(
        Guid staffMemberId,
        CancellationToken ct = default);

    // MEM-08: data for "My library".
    Task<StaffBorrowingSummary> GetStaffSummaryAsync(
        Guid staffMemberId,
        CancellationToken ct = default);
}

public sealed record OpenLoanInfo(
    Guid LoanId,
    Guid BookCopyId,
    Guid BookId,
    DateTime DueAtUtc,
    LoanStatus Status);
