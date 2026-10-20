namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Processes individual candidates for the daily Borrowing job.
/// Each operation owns its transaction.
/// </summary>
public interface IBorrowingDailyJobService
{
    /// <summary>
    /// Marks an eligible Active loan Overdue and publishes LoanOverdue.
    /// Returns false when the loan no longer requires processing.
    /// </summary>
    Task<bool> MarkLoanOverdueAsync(
        Guid loanId,
        CancellationToken ct = default);

    /// <summary>Expires a pickup reservation and releases or reoffers its held copy.</summary>
    Task<bool> ExpirePickupAsync(
        Guid reservationId,
        CancellationToken ct = default);

    /// <summary>
    /// Expires a staff member's penalties and recalculates their status.
    /// </summary>
    Task<PenaltyRecalculationResult> RecalculateStaffAsync(
        Guid staffMemberId,
        CancellationToken ct = default);
}
