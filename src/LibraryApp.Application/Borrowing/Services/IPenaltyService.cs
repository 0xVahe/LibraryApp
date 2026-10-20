using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Penalty operations inside the caller's transaction.
/// The caller must hold the staff member's application lock.
/// </summary>
public interface IPenaltyService
{
    /// <summary>
    /// Creates the supplied penalties and publishes events for positive points.
    /// Does not save, commit, or open a transaction.
    /// </summary>
    Task<PenaltyApplicationResult> ApplyAsync(
        Loan loan,
        IReadOnlyList<ReturnPenaltyItem> items,
        DateTime nowUtc,
        CancellationToken ct = default);
    
    /// <summary>
    /// Expires due penalty records and recalculates staff standing.
    /// The caller must hold the staff lock and own the transaction.
    /// Does not save, commit, or open a transaction.
    /// </summary>
    Task<PenaltyRecalculationResult> RecalculateStatusAsync(
        Guid staffMemberId,
        DateTime nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Waives an active borrowing penalty by its unique identifier and records the waiver reason.
    /// </summary>
    /// <param name="penaltyId">The unique identifier of the penalty record.</param>
    /// <param name="reason">The mandatory explanation for the waiver.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task WaivePenaltyAsync(
        Guid penaltyId,
        string reason,
        CancellationToken cancellationToken = default);
}

public sealed record PenaltyApplicationResult(
    int PointsApplied,
    int ActivePointsTotal);

public sealed record PenaltyRecalculationResult(
    int ExpiredRecords,
    int ActivePointsTotal,
    bool StatusChanged);