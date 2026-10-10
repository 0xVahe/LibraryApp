using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Application.Borrowing.Validators;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Implements the loan use cases. For now: issuing a loan.
/// </summary>
public sealed class LoanService(
    IStaffMemberService staffMembers,
    ICatalogService catalog,
    ILoanRepository loans,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IClock clock,
    IIdGenerator idGenerator)
    : ILoanService
{
    /// <inheritdoc />

    public async Task<LoanDto> IssueAsync(  
        IssueLoanRequest request,
        CancellationToken ct = default)
    {
        IssueLoanRequestValidator.Validate(request);
        
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
            Loan newLoan = Loan.Issue(
                idGenerator.NewId(),
                copy.Id,
                request.StaffMemberId,
                clock.UtcNow,
                rules.LoanDays);

            await loans.AddAsync(newLoan, token);

            // 7. Update the copy within the same transaction.
            await catalog.SetCopyStatusAsync(
                copy.Id, BookCopyStatus.OnLoan, token);

            // Task 6: publish LoanIssued here when using Outbox.

            return newLoan;
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