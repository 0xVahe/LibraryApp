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

    public async Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(Guid bookId, CancellationToken ct = default) =>
        await db.Reservations
            .Where(reservation => reservation.BookId == bookId && reservation.Status == ReservationStatus.Waiting)
            .OrderBy(reservation => reservation.QueuePosition)
            .ThenBy(reservation => reservation.CreatedAtUtc)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(ct);

    public Task<Reservation?> FindReadyForCopyAsync(Guid copyId, CancellationToken ct = default) =>
        db.Reservations.SingleOrDefaultAsync(
            reservation => reservation.CopyId == copyId
                           && reservation.Status == ReservationStatus.ReadyForPickup,
            ct);

    public Task<Reservation?> FindActiveForStaffAndBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) =>
        db.Reservations.SingleOrDefaultAsync(
            reservation => reservation.StaffMemberId == staffMemberId
                           && reservation.BookId == bookId
                           && (reservation.Status == ReservationStatus.Waiting
                               || reservation.Status == ReservationStatus.ReadyForPickup),
            ct);

    public async Task<IReadOnlyList<Reservation>> FindActiveForStaffAsync(Guid staffMemberId, CancellationToken ct = default) =>
        await db.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.StaffMemberId == staffMemberId
                                  && (reservation.Status == ReservationStatus.Waiting
                                      || reservation.Status == ReservationStatus.ReadyForPickup))
            .OrderBy(reservation => reservation.BookId)
            .ToListAsync(ct);

    public Task FlushAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task AddAsync(Reservation reservation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await db.Reservations.AddAsync(reservation, ct);
    }

    public Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default) =>
        db.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reservationId, ct);

    public Task<Reservation?> GetForUpdateAsync(Guid reservationId, CancellationToken ct = default) =>
        db.Reservations.SingleOrDefaultAsync(r => r.Id == reservationId, ct);
}
