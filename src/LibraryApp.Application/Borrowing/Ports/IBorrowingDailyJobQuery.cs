namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>
/// Read-only queries used to find candidates for the daily Borrowing job.
/// Each candidate must be checked again inside its own transaction.
/// </summary>
public interface IBorrowingDailyJobQuery
{
    /// <summary>
    /// Finds Active loans whose due timestamp is strictly before the cutoff.
    /// The caller calculates the cutoff using the configured overdue rule.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveLoanIdsDueBeforeAsync(
        DateTime cutoffUtc,
        CancellationToken ct = default);

    /// <summary>Finds ReadyForPickup reservations whose pickup deadline is strictly before now.</summary>
    Task<IReadOnlyList<Guid>> GetReadyReservationIdsPastDeadlineAsync(
        DateTime nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Finds staff members with at least one Active penalty record: the only ones
    /// whose points can expire or whose suspension can end.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetStaffIdsWithActivePenaltiesAsync(
        CancellationToken ct = default);

    /// <summary>
    /// The books that have Waiting reservations; the job asks Catalog whether each one was deleted.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetBookIdsWithWaitingReservationsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// The staff members with Waiting or ReadyForPickup reservations; the job asks Staff whether each one was
    /// deactivated.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetStaffIdsWithActiveReservationsAsync(
        CancellationToken ct = default);
}
