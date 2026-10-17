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
}

public sealed record PenaltyApplicationResult(
    int PointsApplied,
    int ActivePointsTotal);