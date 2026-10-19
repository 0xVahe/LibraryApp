using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>Storage operations used by reservation use cases. Saving belongs to the unit of work.</summary>
public interface IReservationRepository
{
    Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default);
    Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default);
    Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default);

    /// <summary>
    /// The Waiting reservations of the book in queue order (position, then creation time), tracked so the unit of work
    /// saves their changes.
    /// </summary>
    Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(Guid bookId, CancellationToken ct = default);

    /// <summary>Loads a tracked ReadyForPickup reservation holding the specified copy, if any.</summary>
    Task<Reservation?> FindReadyForCopyAsync(Guid copyId, CancellationToken ct = default);

    /// <summary>
    /// Loads the tracked active (Waiting or ReadyForPickup) reservation of this staff member for this book, if any; there is
    /// at most one (BR-BOR-2, unique index).
    /// </summary>
    Task<Reservation?> FindActiveForStaffAndBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default);

    Task AddAsync(Reservation reservation, CancellationToken ct = default);

    /// <summary>The reservation with this id, read only; null when there is none.</summary>
    Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default);
}
