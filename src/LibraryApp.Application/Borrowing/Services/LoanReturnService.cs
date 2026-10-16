using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Borrowing.Services;

public sealed class LoanReturnService(
    ILoanRepository loans,
    ICatalogService catalog,
    IPenaltyService penalties,
    ReturnPenaltyCalculator calculator,
    ReservationQueueService queue,
    IUnitOfWork unitOfWork,
    IApplicationLock locks,
    IEventBus events,
    IClock clock,
    IIdGenerator ids) : ILoanReturnService
{
    public async Task<ReturnLoanResponse> ReturnAsync(
        Guid loanId,
        ReturnLoanRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Read only to identify whose lock we need.
                Loan initial = await loans.FindAsync(loanId, token)
                    ?? throw new DomainException(
                        BorrowingErrors.LoanNotFound);

                await locks.AcquireAsync(
                    BorrowingLocks.Staff(initial.StaffMemberId),
                    token);

                // Read again after acquiring the lock.
                // This tracked instance is the one we will modify.
                Loan loan = await loans.GetForUpdateAsync(loanId, token)
                    ?? throw new DomainException(
                        BorrowingErrors.LoanNotFound);

                if (!loan.IsOpen)
                {
                    throw new DomainException(
                        BorrowingErrors.LoanAlreadyClosed);
                }

                IReadOnlyList<BookCopyDto> copies =
                    await catalog.GetCopiesAsync(
                        new[] { loan.BookCopyId },
                        token);

                BookCopyDto copy =
                    copies.SingleOrDefault(x => x.Id == loan.BookCopyId)
                    ?? throw new DomainException(
                        BorrowingErrors.CopyNotFound);

                // Always staff first, then book.
                await locks.AcquireAsync(
                    BorrowingLocks.Book(copy.BookId),
                    token);

                DateTime nowUtc = clock.UtcNow;

                ReturnPenaltyCalculation calculation =
                    calculator.Calculate(
                        loan.DueAtUtc,
                        nowUtc,
                        request.Damaged);

                loan.Return(nowUtc, request.Note);;

                if (request.Damaged)
                {
                    await catalog.SetCopyStatusAsync(
                        loan.BookCopyId,
                        BookCopyStatus.UnderRepair,
                        token);
                }
                else
                {
                    bool assigned = await queue.TryOfferCopyAsync(
                        loan.BookCopyId,
                        copy.BookId,
                        token);

                    if (!assigned)
                    {
                        await catalog.SetCopyStatusAsync(
                            loan.BookCopyId,
                            BookCopyStatus.Available,
                            token);
                    }
                }

                PenaltyApplicationResult penaltyResult =
                    await penalties.ApplyAsync(
                        loan,
                        calculation.Penalties,
                        nowUtc,
                        token);


                await events.PublishAsync(
                    new LoanReturned(
                        ids.NewId(),
                        nowUtc,
                        loan.Id,
                        loan.StaffMemberId,
                        loan.BookCopyId,
                        copy.BookId,
                        nowUtc,
                        calculation.OverdueWorkingDays),
                    token);

                return new ReturnLoanResponse(
                    loan.Id,
                    nowUtc,
                    loan.Status,
                    calculation.OverdueWorkingDays,
                    penaltyResult.PointsApplied);
            },
            ct);
    }
}