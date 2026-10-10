using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>Storage operations used by reservation use cases. Saving belongs to the unit of work.</summary>
public interface IReservationRepository
{
    Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default);
    Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default);
    Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default);
    Task AddAsync(Reservation reservation, CancellationToken ct = default);
}
