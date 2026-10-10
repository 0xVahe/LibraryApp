using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Borrowing-owned read contract. Staff uses it for BR-MEM-4; it never opens a transaction.</summary>
public interface IBorrowingReadService
{
    /// <summary>All Active and Overdue loans; Returned and Lost loans are excluded. No Catalog calls or paging.</summary>
    Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default);
}

public sealed record OpenLoanInfo(
    Guid LoanId,
    Guid StaffMemberId,
    Guid BookCopyId,
    Guid BookId,
    DateTime DueAtUtc,
    LoanStatus Status);
