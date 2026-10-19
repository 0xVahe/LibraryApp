using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Processes one daily-job candidate inside a short transaction.
/// The daily job resolves a fresh service scope for each candidate.
/// </summary>
public sealed class BorrowingDailyJobService(
    ILoanRepository loans,
    ICatalogService catalog,
    IPenaltyService penalties,
    OverdueRule overdueRule,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IEventBus events,
    IClock clock,
    IIdGenerator ids,
    IReservationRepository reservations,
    ReservationQueueService reservationQueue) : IBorrowingDailyJobService
{
    public Task<bool> ExpirePickupAsync(
        Guid reservationId,
        CancellationToken ct = default)
    {
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Read only to identify the book lock.
                Reservation? initial = await reservations.FindAsync(
                    reservationId,
                    token);

                if (initial is null
                    || initial.Status != ReservationStatus.ReadyForPickup)
                {
                    return false;
                }

                await locks.AcquireAsync(
                    BorrowingLocks.Book(initial.BookId),
                    token);

                // Reload after acquiring the lock. Another job run or pickup may
                // have completed while this reservation waited for the lock.
                Reservation? reservation = await reservations.GetForUpdateAsync(
                    reservationId,
                    token);

                if (reservation is null
                    || reservation.Status != ReservationStatus.ReadyForPickup)
                {
                    return false;
                }

                DateTime nowUtc = clock.UtcNow;
                if (reservation.ReadyUntilUtc is not { } readyUntilUtc
                    || nowUtc <= readyUntilUtc)
                {
                    return false;
                }

                Guid copyId = reservation.CopyId
                    ?? throw new InvalidOperationException(
                        "A ReadyForPickup reservation must have a copy.");

                reservation.Expire(nowUtc);

                // Free the unique ReadyForPickup/CopyId slot before offering
                // the same physical copy to the next reservation.
                await reservations.FlushAsync(token);

                bool offered = await reservationQueue
                    .TryOfferCopyUnderBookLockAsync(
                        copyId,
                        reservation.BookId,
                        BookCopyStatus.Reserved,
                        token);

                if (!offered)
                {
                    await catalog.SetCopyStatusAsync(
                        copyId,
                        BookCopyStatus.Available,
                        token);
                }

                return true;
            },
            ct);
    }

    public Task<bool> MarkLoanOverdueAsync(
        Guid loanId,
        CancellationToken ct = default)
    {
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Read only to identify the staff lock.
                Loan? initial = await loans.FindAsync(loanId, token);

                if (initial is null)
                {
                    return false;
                }

                await locks.AcquireAsync(
                    BorrowingLocks.Staff(initial.StaffMemberId),
                    token);

                // Reload after acquiring the lock.
                // A return or renewal may have happened since selection.
                Loan? loan = await loans.GetForUpdateAsync(
                    loanId,
                    token);

                if (loan is null || loan.Status != LoanStatus.Active)
                {
                    return false;
                }

                // LoanOverdue requires BookId.
                // Loans store BookCopyId, so resolve it through Catalog.
                var copies = await catalog.GetCopiesAsync(
                    new[] { loan.BookCopyId },
                    token);

                var copy = copies.SingleOrDefault(
                    candidate => candidate.Id == loan.BookCopyId);

                if (copy is null)
                {
                    throw new DomainException(
                        BorrowingErrors.CopyNotFound);
                }

                // Preserve the agreed order: staff first, then book.
                await locks.AcquireAsync(
                    BorrowingLocks.Book(copy.BookId),
                    token);

                DateTime nowUtc = clock.UtcNow;

                // Use the same rule as the return flow.
                if (!overdueRule.IsOverdue(loan.DueAtUtc, nowUtc))
                {
                    return false;
                }

                loan.MarkOverdue(nowUtc);

                await events.PublishAsync(
                    new LoanOverdue(
                        ids.NewId(),
                        nowUtc,
                        loan.Id,
                        loan.StaffMemberId,
                        copy.BookId,
                        loan.DueAtUtc),
                    token);

                return true;
            },
            ct);
    }

    public Task<PenaltyRecalculationResult> RecalculateStaffAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await locks.AcquireAsync(
                    BorrowingLocks.Staff(staffMemberId),
                    token);

                return await penalties.RecalculateStatusAsync(
                    staffMemberId,
                    clock.UtcNow,
                    token);
            },
            ct);
    }
}
