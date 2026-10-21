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
    /// When Catalog reports the book deleted (or unknown), cancels its Waiting reservations under the
    /// book lock, so they no longer count towards the member's limit (BR-BOR-2). Returns how many were cancelled.
    /// </summary>
    Task<int> CancelWaitingReservationsOfDeletedBookAsync(
        Guid bookId,
        CancellationToken ct = default);

    /// <summary>
    /// BR-MEM-4: when Staff reports the member Deactivated, cancels their Waiting and ReadyForPickup reservations
    /// under the staff lock and each book lock; a held copy goes to the next waiting member or becomes Available.
    /// Returns how many were cancelled.
    /// </summary>
    Task<int> CancelReservationsOfDeactivatedMemberAsync(
        Guid staffMemberId,
        CancellationToken ct = default);

    /// <summary>
    /// Expires a staff member's penalties and recalculates their status.
    /// </summary>
    Task<PenaltyRecalculationResult> RecalculateStaffAsync(
        Guid staffMemberId,
        CancellationToken ct = default);
}
