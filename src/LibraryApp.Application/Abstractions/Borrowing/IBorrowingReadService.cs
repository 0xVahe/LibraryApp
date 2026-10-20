using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Borrowing-owned read contract for other modules; it never opens a transaction.</summary>
public interface IBorrowingReadService
{
    /// <summary>All Active and Overdue loans; Returned and Lost loans are excluded. No Catalog calls or paging. Staff uses it for BR-MEM-4.</summary>
    Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default);

    /// <summary>
    /// MEM-08 "My library": open loans (earliest due date first), Waiting and ReadyForPickup reservations and
    /// active penalties (oldest first). Ids only; Staff adds titles from Catalog.
    /// </summary>
    Task<StaffBorrowingSummary> GetStaffSummaryAsync(Guid staffMemberId, CancellationToken ct = default);

    /// <summary>
    /// NOT-05: the open loans due on this local Asia/Yerevan date, earliest first, for the 09:00 due-soon
    /// reminder of Notifications.
    /// </summary>
    Task<IReadOnlyList<OpenLoanInfo>> GetLoansDueOnAsync(DateOnly localDate, CancellationToken ct = default);
}

public sealed record OpenLoanInfo(
    Guid LoanId,
    Guid StaffMemberId,
    Guid BookCopyId,
    Guid BookId,
    DateTime DueAtUtc,
    LoanStatus Status,
    DateTime IssuedAtUtc,
    int RenewalCount);
