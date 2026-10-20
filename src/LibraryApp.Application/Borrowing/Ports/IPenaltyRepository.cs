using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>
/// Stores penalty records. Saving belongs to the unit of work.
/// </summary>
public interface IPenaltyRepository
{
    /// <summary>
    /// Loads a single penalty record by its unique identifier.
    /// </summary>
    Task<PenaltyRecord?> GetByIdAsync(Guid penaltyId, CancellationToken ct = default);

    /// <summary>
    /// Loads the staff member's existing penalty records, tracked for changes.
    /// Includes active, expired and waived records.
    /// </summary>
    Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(
        Guid staffMemberId,
        CancellationToken ct = default);

    /// <summary>
    /// Stages a new penalty record for insertion.
    /// </summary>
    Task AddAsync(
        PenaltyRecord penalty,
        CancellationToken ct = default);
}