using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// Finds candidates for the daily Borrowing job.
/// Does not modify entities, save changes, or open transactions.
/// </summary>
public sealed class BorrowingDailyJobQuery(LibraryDbContext db)
    : IBorrowingDailyJobQuery
{
    public async Task<IReadOnlyList<Guid>> GetActiveLoanIdsDueBeforeAsync(
        DateTime cutoffUtc,
        CancellationToken ct = default)
    {
        if (cutoffUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The overdue cutoff must be UTC.",
                nameof(cutoffUtc));
        }

        return await db.Loans
            .AsNoTracking()
            .Where(loan =>
                loan.Status == LoanStatus.Active
                && loan.DueAtUtc < cutoffUtc)
            .OrderBy(loan => loan.DueAtUtc)
            .ThenBy(loan => loan.Id)
            .Select(loan => loan.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetStaffIdsWithActivePenaltiesAsync(
        CancellationToken ct = default)
    {
        return await db.PenaltyRecords
            .AsNoTracking()
            .Where(penalty => penalty.Status == PenaltyRecordStatus.Active)
            .Select(penalty => penalty.StaffMemberId)
            .Distinct()
            .OrderBy(staffMemberId => staffMemberId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetBookIdsWithWaitingReservationsAsync(
        CancellationToken ct = default)
    {
        return await db.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.Status == ReservationStatus.Waiting)
            .Select(reservation => reservation.BookId)
            .Distinct()
            .OrderBy(bookId => bookId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetStaffIdsWithActiveReservationsAsync(
        CancellationToken ct = default)
    {
        return await db.Reservations
            .AsNoTracking()
            .Where(reservation =>
                reservation.Status == ReservationStatus.Waiting
                || reservation.Status == ReservationStatus.ReadyForPickup)
            .Select(reservation => reservation.StaffMemberId)
            .Distinct()
            .OrderBy(staffMemberId => staffMemberId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetReadyReservationIdsPastDeadlineAsync(
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The pickup expiry time must be UTC.",
                nameof(nowUtc));
        }

        return await db.Reservations
            .AsNoTracking()
            .Where(reservation =>
                reservation.Status == ReservationStatus.ReadyForPickup
                && reservation.ReadyUntilUtc < nowUtc)
            .OrderBy(reservation => reservation.ReadyUntilUtc)
            .ThenBy(reservation => reservation.Id)
            .Select(reservation => reservation.Id)
            .ToListAsync(ct);
    }
}
