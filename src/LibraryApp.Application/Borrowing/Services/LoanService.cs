using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Implements the loan use cases: issuing a copy and reading a loan.
/// The input is validated by <c>IssueLoanRequestValidator</c> before the endpoint is reached (SD-15).
/// </summary>
public sealed class LoanService(
    IStaffMemberService staffMembers,
    ICatalogService catalog,
    ILoanRepository loans,
    IReservationRepository reservations,
    ReservationQueueService reservationQueue,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IEventBus events,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<BorrowingOptions> options)
    : ILoanService
{
    /// <inheritdoc />
    public async Task<LoanDto> IssueAsync(
        IssueLoanRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Loan loan = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // 0. The staff lock deactivation also takes (BR-MEM-4): no loan is issued while the employee is being
            //    deactivated, and two parallel issues cannot both pass the loan limit (SD-10).
            await locks.AcquireAsync(BorrowingLocks.Staff(request.StaffMemberId), token);

            // 1. Check the employee's status.
            EligibilityResult eligibility =
                await staffMembers.CanBorrowAsync(request.StaffMemberId, token);

            if (!eligibility.IsAllowed)
                throw new DomainException(eligibility.Error!);

            // 2. Get the employee's lending limits.
            LendingRules rules =
                await staffMembers.GetLendingRulesAsync(request.StaffMemberId, token);

            // 3. Count Active + Overdue loans before looking up the copy.
            int openLoans =
                await loans.CountOpenLoansAsync(request.StaffMemberId, token);

            if (openLoans >= rules.MaxLoans)
                throw new DomainException(
                    StaffErrors.LoanLimitReached(openLoans, rules.MaxLoans));

            // 4. Find the copy to identify its book, then take the book lock after the staff lock.
            BookCopyDto copy =
                await catalog.GetCopyByBarcodeAsync(request.Barcode, token)
                ?? throw new DomainException(BorrowingErrors.CopyNotFound);

            await locks.AcquireAsync(BorrowingLocks.Book(copy.BookId), token);

            // Re-read after waiting for the book lock so we act on the current copy state.
            copy = await catalog.GetCopyByBarcodeAsync(request.Barcode, token)
                ?? throw new DomainException(BorrowingErrors.CopyNotFound);

            // 5. A held copy can be collected only by the member whose ReadyForPickup reservation owns it.
            Reservation? heldReservation = null;
            if (copy.Status == BookCopyStatus.Reserved)
            {
                heldReservation = await reservations.FindReadyForCopyAsync(copy.Id, token);
                if (heldReservation is null || heldReservation.StaffMemberId != request.StaffMemberId)
                    throw new DomainException(BorrowingErrors.CopyNotAvailable);

                heldReservation.Fulfil();
            }
            else if (copy.Status != BookCopyStatus.Available)
                throw new DomainException(BorrowingErrors.CopyNotAvailable);

            // BQ-BA-08 (setting Borrowing:Reservations:AnyCopyFulfils): issuing any copy of the book closes this
            // member's active reservation for it, so no copy is held later for someone who already has the book.
            if (heldReservation is null && copy.Status == BookCopyStatus.Available && options.Value.Reservations.AnyCopyFulfils)
                await CloseReservationForIssuedBookAsync(request.StaffMemberId, copy.BookId, token);

            // 6. Create and stage the loan.
            DateTime now = clock.UtcNow;
            Loan newLoan = Loan.Issue(
                idGenerator.NewId(),
                copy.Id,
                request.StaffMemberId,
                now,
                rules.LoanDays);

            await loans.AddAsync(newLoan, token);

            // 7. Update the copy within the same transaction.
            await catalog.SetCopyStatusAsync(
                copy.Id, BookCopyStatus.OnLoan, token);

            // 8. LoanIssued (INT-07) is stored in the same transaction (outbox): it is delivered only if the loan is saved.
            await events.PublishAsync(new LoanIssued(
                idGenerator.NewId(),
                now,
                newLoan.Id,
                newLoan.StaffMemberId,
                newLoan.BookCopyId,
                copy.BookId,
                newLoan.DueAtUtc), token);

            return newLoan;
        }, ct);

        return ToDto(loan);
    }

    /// <summary>
    /// A ready reservation is fulfilled and its held copy goes to the next waiting member, or becomes Available when
    /// nobody waits. A waiting one is cancelled (Fulfil needs a held copy) and the members behind it move up.
    /// Runs under the staff and book locks, inside the issue transaction.
    /// </summary>
    private async Task CloseReservationForIssuedBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct)
    {
        Reservation? active = await reservations.FindActiveForStaffAndBookAsync(staffMemberId, bookId, ct);
        if (active is { Status: ReservationStatus.ReadyForPickup, CopyId: Guid heldCopyId })
        {
            active.Fulfil();
            // The held copy may now be held for the next member: this reservation leaves ReadyForPickup first.
            await reservations.FlushAsync(ct);
            bool reassigned = await reservationQueue.TryOfferCopyUnderBookLockAsync(
                heldCopyId, bookId, BookCopyStatus.Reserved, ct);
            if (!reassigned)
                await catalog.SetCopyStatusAsync(heldCopyId, BookCopyStatus.Available, ct);
        }
        else if (active is { Status: ReservationStatus.Waiting })
        {
            active.Cancel();
            // The query still returns the cancelled row (not saved yet), as the tracked entity with its new status.
            IReadOnlyList<Reservation> waiting = await reservations.GetWaitingForBookAsync(bookId, ct);
            int position = 1;
            foreach (Reservation next in waiting.Where(r => r.Status == ReservationStatus.Waiting))
                next.SetQueuePosition(position++);
        }
    }

    /// <inheritdoc />
    public async Task<LoanDto> GetAsync(Guid loanId, CancellationToken ct = default) =>
        ToDto(await loans.FindAsync(loanId, ct) ?? throw new DomainException(BorrowingErrors.LoanNotFound));

    /// <inheritdoc />
    public async Task<LoanDto> FindOpenByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        BookCopyDto copy = await catalog.GetCopyByBarcodeAsync(barcode, ct)
                           ?? throw new DomainException(BorrowingErrors.CopyNotFound);

        return ToDto(await loans.FindOpenByCopyAsync(copy.Id, ct)
                     ?? throw new DomainException(BorrowingErrors.NoOpenLoanForCopy));
    }

    /// <inheritdoc />
    public async Task<LoanDto> RenewAsync(
        Guid loanId,
        CancellationToken ct = default)
    {
        if (loanId == Guid.Empty)
            throw new DomainException(BorrowingErrors.LoanNotFound);

        Loan loan = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Loan currentLoan = await loans.GetForUpdateAsync(loanId, token)
                ?? throw new DomainException(BorrowingErrors.LoanNotFound);

            await locks.AcquireAsync(
                BorrowingLocks.Staff(currentLoan.StaffMemberId),
                token);

            EligibilityResult eligibility =
                await staffMembers.CanBorrowAsync(
                    currentLoan.StaffMemberId,
                    token);

            if (!eligibility.IsAllowed)
                throw new DomainException(eligibility.Error!);

            LendingRules rules =
                await staffMembers.GetLendingRulesAsync(
                    currentLoan.StaffMemberId,
                    token);

            currentLoan.Renew(rules.LoanDays);

            return currentLoan;
        }, ct);

        return ToDto(loan);
    }

    // Maps the entity to the DTO. Entities never leave the Application layer.
    private static LoanDto ToDto(Loan loan) => new(
        loan.Id,
        loan.BookCopyId,
        loan.StaffMemberId,
        loan.IssuedAtUtc,
        loan.DueAtUtc,
        loan.RenewalCount,
        loan.Status);
}
