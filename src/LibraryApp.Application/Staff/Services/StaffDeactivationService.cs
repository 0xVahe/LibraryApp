using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Logging;

namespace LibraryApp.Application.Staff.Services;

public sealed class StaffDeactivationService(
    IStaffManagementRepository members,
    IStaffAccountStore accounts,
    IBorrowingReadService borrowing,
    ICatalogService catalog,
    IApplicationLock locks,
    IUnitOfWork unitOfWork,
    IEventBus events,
    IClock clock,
    IIdGenerator ids,
    ILogger<StaffDeactivationService> logger) : IStaffDeactivationService
{
    public Task DeactivateAsync(Guid id, DeactivateStaffMemberRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await locks.AcquireAsync(BorrowingLocks.Staff(id), token);
            var member = await members.GetByIdAsync(id, token) ?? throw new DomainException(StaffErrors.NotFound);
            if (member.Status == StaffMemberStatus.Deactivated)
                throw new DomainException(StaffErrors.AlreadyDeactivated);

            var loans = await borrowing.GetActiveLoansAsync(id, token);
            if (loans.Count > 0)
            {
                var books = await GetOutstandingBooksAsync(loans, token);
                throw new DomainException(StaffErrors.HasActiveLoans,
                    extensions: new Dictionary<string, object?> { ["outstandingBooks"] = books });
            }

            var oldStatus = member.Status;
            member.Deactivate();
            await accounts.DeactivateAsync(member.UserId, token);
            await events.PublishAsync(new StaffStatusChanged(ids.NewId(), clock.UtcNow, member.Id,
                oldStatus, member.Status, request.Reason.Trim(), null), token);
        }, ct);

    private async Task<IReadOnlyList<OutstandingBookDto>> GetOutstandingBooksAsync(
        IReadOnlyList<OpenLoanInfo> loans, CancellationToken ct)
    {
        var copies = (await catalog.GetCopiesAsync(loans.Select(x => x.BookCopyId).Distinct().ToArray(), ct))
            .ToDictionary(x => x.Id);
        var titles = new Dictionary<Guid, string>();
        foreach (var bookId in loans.Select(x => x.BookId).Distinct())
        {
            var book = await catalog.GetBookAsync(bookId, ct);
            if (book is null)
            {
                logger.LogError("Cannot resolve book {BookId} for outstanding loan {LoanId}",
                    bookId, loans.First(x => x.BookId == bookId).LoanId);
                throw new InvalidOperationException("An outstanding loan's book could not be resolved.");
            }
            titles.Add(bookId, book.Title);
        }

        return loans.Select(loan =>
        {
            if (!copies.TryGetValue(loan.BookCopyId, out var copy) || copy.BookId != loan.BookId)
            {
                logger.LogError("Cannot resolve copy {BookCopyId} for outstanding loan {LoanId}", loan.BookCopyId, loan.LoanId);
                throw new InvalidOperationException("An outstanding loan's copy could not be resolved.");
            }
            return new OutstandingBookDto(titles[loan.BookId], copy.Barcode);
        }).ToArray();
    }
}
