using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// BOR-04: a renewal moves <c>DueAtUtc</c> by the category's <c>LoanDays</c>, adds one to <c>RenewalCount</c> and
/// publishes <c>LoanRenewed</c>, in one transaction (BR-BOR-4) under the staff lock, then the book lock (the order of
/// issue and return). BR-BOR-1 refusals, all 422, in this order: the loan is Overdue (or past the overdue rule before
/// the daily job marked it); the employee is Restricted (BR-MEM-3) or Suspended (unless
/// <c>Borrowing:Loans:SuspendedMayRenew</c>); <c>RenewalCount</c> reached <c>MaxRenewals</c>; another employee has a
/// Waiting reservation for the book. The status comes before the count: Staff gives a Restricted employee
/// <c>MaxRenewals = 0</c>, and the status is the more precise reason.
/// </summary>
public sealed class LoanRenewalService(
    ILoanRepository loans,
    IReservationRepository reservations,
    ICatalogService catalog,
    IStaffMemberService staffMembers,
    OverdueRule overdueRule,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IEventBus events,
    ICurrentUser currentUser,
    IClock clock,
    IIdGenerator ids,
    IOptions<BorrowingOptions> options) : ILoanRenewalService
{
    public async Task<LoanDto> RenewAsync(Guid loanId, CancellationToken ct = default)
    {
        Loan renewed = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Read only, to find whose lock is needed; NFR-03 is checked before waiting on it.
            Loan initial = await loans.FindAsync(loanId, token)
                ?? throw new DomainException(BorrowingErrors.LoanNotFound);

            EnsureCanManage(initial);

            await locks.AcquireAsync(BorrowingLocks.Staff(initial.StaffMemberId), token);

            // Read again under the lock: a return, a lost declaration or another renewal may have happened meanwhile.
            Loan loan = await loans.GetForUpdateAsync(loanId, token)
                ?? throw new DomainException(BorrowingErrors.LoanNotFound);

            if (!loan.IsOpen)
                throw new DomainException(BorrowingErrors.LoanCannotBeRenewed);

            DateTime nowUtc = clock.UtcNow;

            if (loan.Status == LoanStatus.Overdue || overdueRule.IsOverdue(loan.DueAtUtc, nowUtc))
                throw new DomainException(BorrowingErrors.LoanOverdueCannotBeRenewed);

            StaffMemberStatus status = await staffMembers.GetStatusAsync(loan.StaffMemberId, token);
            switch (status)
            {
                case StaffMemberStatus.Restricted:
                    throw new DomainException(BorrowingErrors.RestrictedRenewalForbidden);
                case StaffMemberStatus.Suspended when !options.Value.Loans.SuspendedMayRenew:
                    throw new DomainException(BorrowingErrors.SuspendedRenewalForbidden);
                case StaffMemberStatus.Deactivated:
                    // Cannot happen with an open loan (BR-MEM-4); Staff's own reason if it ever does.
                    EligibilityResult eligibility = await staffMembers.CanBorrowAsync(loan.StaffMemberId, token);
                    throw new DomainException(eligibility.Error ?? BorrowingErrors.LoanCannotBeRenewed);
            }

            LendingRules rules = await staffMembers.GetLendingRulesAsync(loan.StaffMemberId, token);
            if (loan.RenewalCount >= rules.MaxRenewals)
                throw new DomainException(BorrowingErrors.MaxRenewalsReached);

            BookCopyDto copy = (await catalog.GetCopiesAsync([loan.BookCopyId], token))
                .SingleOrDefault(candidate => candidate.Id == loan.BookCopyId)
                ?? throw new DomainException(BorrowingErrors.CopyNotFound);

            // Staff first, then book. Under the book lock no reservation can join the queue meanwhile.
            await locks.AcquireAsync(BorrowingLocks.Book(copy.BookId), token);

            IReadOnlyList<Reservation> waiting = await reservations.GetWaitingForBookAsync(copy.BookId, token);
            if (waiting.Any(reservation => reservation.StaffMemberId != loan.StaffMemberId))
                throw new DomainException(BorrowingErrors.RenewalBlockedByReservation);

            loan.Renew(rules.LoanDays);

            await events.PublishAsync(
                new LoanRenewed(
                    ids.NewId(),
                    nowUtc,
                    loan.Id,
                    loan.StaffMemberId,
                    copy.BookId,
                    loan.DueAtUtc,
                    loan.RenewalCount),
                token);

            return loan;
        }, ct);

        return new LoanDto(
            renewed.Id,
            renewed.BookCopyId,
            renewed.StaffMemberId,
            renewed.IssuedAtUtc,
            renewed.DueAtUtc,
            renewed.RenewalCount,
            renewed.Status);
    }

    /// <summary>NFR-03: a Staff Member renews only their own loans; another person's loan is reported as not found.</summary>
    private void EnsureCanManage(Loan loan)
    {
        bool librarian = currentUser.IsInRole(StaffRoles.Librarian) || currentUser.IsInRole(StaffRoles.LibraryAdministrator);
        if (!librarian && currentUser.StaffMemberId != loan.StaffMemberId)
            throw new DomainException(BorrowingErrors.LoanNotFound);
    }
}
