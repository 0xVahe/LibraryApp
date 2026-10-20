using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

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
    ReservationQueueService reservationQueue,
    IStaffMemberService staffMembers) : IBorrowingDailyJobService
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

    public Task<int> CancelWaitingReservationsOfDeletedBookAsync(
        Guid bookId,
        CancellationToken ct = default)
    {
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // BQ-G1-09: Catalog deletes a book without asking Borrowing (only OnLoan/Reserved copies block it,
                // BR-CAT-3), so its queue can never be served. GetBookAsync returns a deleted book with IsDeleted.
                BookDto? book = await catalog.GetBookAsync(bookId, token);
                if (book is { IsDeleted: false })
                {
                    return 0;
                }

                await locks.AcquireAsync(
                    BorrowingLocks.Book(bookId),
                    token);

                // Read under the lock. A ReadyForPickup reservation cannot exist here: its copy is Reserved, and a
                // Reserved copy blocks the delete; the whole queue leaves, so no position is renumbered.
                IReadOnlyList<Reservation> waiting =
                    await reservations.GetWaitingForBookAsync(bookId, token);

                foreach (Reservation reservation in waiting)
                {
                    reservation.Cancel();
                }

                return waiting.Count;
            },
            ct);
    }

    public Task<int> CancelReservationsOfDeactivatedMemberAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // BR-MEM-4 blocks a deactivation only while the member holds loans; their reservations stay, so a copy
                // could be held for someone who never comes. Borrowing never reactivates a member, so the check holds.
                StaffMemberStatus status =
                    await staffMembers.GetStatusAsync(staffMemberId, token);

                if (status != StaffMemberStatus.Deactivated)
                {
                    return 0;
                }

                await locks.AcquireAsync(
                    BorrowingLocks.Staff(staffMemberId),
                    token);

                // Ordered by book id: the book locks are always taken in the same order (staff first, then books).
                IReadOnlyList<Reservation> active =
                    await reservations.FindActiveForStaffAsync(staffMemberId, token);

                int cancelled = 0;

                foreach (Reservation candidate in active)
                {
                    await locks.AcquireAsync(
                        BorrowingLocks.Book(candidate.BookId),
                        token);

                    // Read again under the book lock: the queue may have given this member a copy meanwhile.
                    Reservation? reservation = await reservations.GetForUpdateAsync(
                        candidate.Id,
                        token);

                    if (reservation is null
                        || reservation.Status is not (ReservationStatus.Waiting
                            or ReservationStatus.ReadyForPickup))
                    {
                        continue;
                    }

                    await reservationQueue.CancelUnderLocksAsync(reservation, token);
                    cancelled++;
                }

                return cancelled;
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
