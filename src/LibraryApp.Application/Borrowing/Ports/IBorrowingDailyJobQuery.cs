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

    /// <summary>
    /// Finds staff members with penalty history, including expired and waived
    /// records, so expiry and the end of a suspension can be checked.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetStaffIdsWithPenaltyHistoryAsync(
        CancellationToken ct = default);
}