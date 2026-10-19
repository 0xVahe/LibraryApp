using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Declares a borrowed copy lost using the shared penalty component.
/// </summary>
public sealed class LoanLostService(
    ILoanRepository loans,
    ICatalogService catalog,
    IPenaltyService penalties,
    PenaltyPolicy policy,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IClock clock,
    OverdueRule overdue,
    IOptions<BorrowingOptions> options) : ILoanLostService
{
    public Task<DeclareLostResponse> DeclareLostAsync(
        Guid loanId,
        CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // First identify the staff member whose operations must be locked.
            Loan initial = await loans.FindAsync(loanId, token)
                ?? throw new DomainException(
                    BorrowingErrors.LoanNotFound);

            await locks.AcquireAsync(
                BorrowingLocks.Staff(initial.StaffMemberId),
                token);

            // Reload after acquiring the lock.
            Loan loan = await loans.GetForUpdateAsync(loanId, token)
                ?? throw new DomainException(
                    BorrowingErrors.LoanNotFound);

            if (!loan.IsOpen)
            {
                throw new DomainException(
                    BorrowingErrors.LoanAlreadyClosed);
            }

            DateTime nowUtc = clock.UtcNow;

            if (!options.Value.Loans.AllowLostBeforeDue
                && loan.Status != LoanStatus.Overdue
                && !overdue.IsOverdue(loan.DueAtUtc, nowUtc))
            {
                throw new DomainException(
                    BorrowingErrors.LoanNotPastDue);
            }

            IReadOnlyList<BookCopyDto> copies =
                await catalog.GetCopiesAsync(
                    [loan.BookCopyId],
                    token);

            BookCopyDto copy = copies.SingleOrDefault(
                item => item.Id == loan.BookCopyId)
                ?? throw new DomainException(
                    BorrowingErrors.CopyNotFound);

            // Preserve the existing lock order: staff first, then book.
            await locks.AcquireAsync(
                BorrowingLocks.Book(copy.BookId),
                token);

            loan.MarkLost(nowUtc);

            await catalog.SetCopyStatusAsync(
                loan.BookCopyId,
                BookCopyStatus.Lost,
                token);

            // The shared penalty service creates the penalty and handles
            // PenaltyApplied and any actual StaffStatusChanged event.
            PenaltyApplicationResult result =
                await penalties.ApplyAsync(
                    loan,
                    [
                        new ReturnPenaltyItem(
                            policy.CalculateLostPoints(),
                            "Copy declared lost.")
                    ],
                    nowUtc,
                    token);

            return new DeclareLostResponse(
                loan.Id,
                nowUtc,
                loan.Status,
                result.PointsApplied);
        }, ct);
}