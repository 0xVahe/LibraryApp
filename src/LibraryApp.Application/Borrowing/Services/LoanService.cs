using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Implements the loan use cases. For now: issuing a loan and reading one.
/// The input is validated by <c>IssueLoanRequestValidator</c> before the endpoint is reached (SD-15).
/// </summary>
public sealed class LoanService(
    IStaffMemberService staffMembers,
    ICatalogService catalog,
    ILoanRepository loans,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IEventBus events,
    IClock clock,
    IIdGenerator idGenerator)
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

            // 4. Find the copy by barcode.
            BookCopyDto copy =
                await catalog.GetCopyByBarcodeAsync(request.Barcode, token)
                ?? throw new DomainException(BorrowingErrors.CopyNotFound);

            // 5. Check availability.
            if (copy.Status != BookCopyStatus.Available)
                throw new DomainException(BorrowingErrors.CopyNotAvailable);

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

    /// <inheritdoc />
    public async Task<LoanDto> GetAsync(Guid loanId, CancellationToken ct = default) =>
        ToDto(await loans.FindAsync(loanId, ct) ?? throw new DomainException(BorrowingErrors.LoanNotFound));

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