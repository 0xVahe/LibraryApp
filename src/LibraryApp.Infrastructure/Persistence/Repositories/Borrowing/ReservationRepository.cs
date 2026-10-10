using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>EF Core storage for reservations. It never saves; the unit of work commits the operation.</summary>
public sealed class ReservationRepository(LibraryDbContext db) : IReservationRepository
{
    public Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default) =>
        db.Reservations.AsNoTracking().CountAsync(
            reservation => reservation.StaffMemberId == staffMemberId
                           && (reservation.Status == ReservationStatus.Waiting
                               || reservation.Status == ReservationStatus.ReadyForPickup),
            ct);

    public Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) =>
        db.Reservations.AsNoTracking().AnyAsync(
            reservation => reservation.StaffMemberId == staffMemberId
                           && reservation.BookId == bookId
                           && (reservation.Status == ReservationStatus.Waiting
                               || reservation.Status == ReservationStatus.ReadyForPickup),
            ct);

    public Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default) =>
        db.Reservations.AsNoTracking().CountAsync(
            reservation => reservation.BookId == bookId
                           && reservation.Status == ReservationStatus.Waiting,
            ct);

    public async Task AddAsync(Reservation reservation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await db.Reservations.AddAsync(reservation, ct);
    }
}
