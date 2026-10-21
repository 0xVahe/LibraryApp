using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>
/// Stores penalty records. Saving belongs to the unit of work.
/// </summary>
public interface IPenaltyRepository
{
    /// <summary>
    /// Loads the staff member's existing penalty records, tracked for changes.
    /// Includes active, expired and waived records.
    /// </summary>
    Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(
        Guid staffMemberId,
        CancellationToken ct = default);

    /// <summary>The record with this id, read only; null when there is none.</summary>
    Task<PenaltyRecord?> FindAsync(Guid penaltyId, CancellationToken ct = default);

    /// <summary>Loads a tracked record for a change made inside the caller's unit of work.</summary>
    Task<PenaltyRecord?> GetForUpdateAsync(Guid penaltyId, CancellationToken ct = default);

    /// <summary>
    /// One page of the staff member's records, newest first, read only; <paramref name="status"/> narrows the list.
    /// </summary>
    Task<PagedResult<PenaltyRecord>> PageForStaffAsync(
        Guid staffMemberId,
        PenaltyRecordStatus? status,
        PageRequest page,
        CancellationToken ct = default);

    /// <summary>
    /// Stages a new penalty record for insertion.
    /// </summary>
    Task AddAsync(
        PenaltyRecord penalty,
        CancellationToken ct = default);
}