using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>Loan history per staff member and per book, with filters and paging (BOR-10).</summary>
public interface ILoanHistoryService
{
    Task<PagedResult<LoanHistoryItemDto>> ListAsync(
        LoanHistoryFilter filter,
        PageRequest page,
        CancellationToken ct = default);
}

/// <summary>
/// NFR-03: a Staff Member gets only their own loans, whatever the filter says; a Librarian or Library Administrator
/// gets any. A book's loans are found through its copies (<see cref="ICatalogService.GetCopyIdsAsync"/>, retired
/// copies included), so the history of a book stays complete after a copy is withdrawn or the book is deleted.
/// The issue period is a pair of local dates, both days included; a period that ends before it starts is refused
/// (<c>borrowing.history_period_invalid</c>).
/// </summary>
public sealed class LoanHistoryService(
    ILoanHistoryQuery history,
    ICatalogService catalog,
    ICurrentUser currentUser,
    IClock clock) : ILoanHistoryService
{
    public async Task<PagedResult<LoanHistoryItemDto>> ListAsync(
        LoanHistoryFilter filter,
        PageRequest page,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        if (filter.IssuedFrom is { } from && filter.IssuedTo is { } to && from > to)
            throw new DomainException(BorrowingErrors.HistoryPeriodInvalid);

        bool librarian = currentUser.IsInRole(StaffRoles.Librarian) || currentUser.IsInRole(StaffRoles.LibraryAdministrator);

        // A Staff Member without a staff profile sees nothing rather than everyone's loans.
        Guid? staffMemberId = librarian ? filter.StaffMemberId : currentUser.StaffMemberId ?? Guid.Empty;

        IReadOnlyCollection<Guid>? copyIds = filter.BookId is { } bookId
            ? await catalog.GetCopyIdsAsync(bookId, ct)
            : null;

        DateTime? issuedFromUtc = filter.IssuedFrom is { } start ? clock.StartOfLocalDayUtc(start) : null;
        DateTime? issuedToUtc = filter.IssuedTo is { } end ? clock.EndOfLocalDayUtc(end) : null;

        return await history.PageAsync(staffMemberId, copyIds, filter.Status, issuedFromUtc, issuedToUtc, page, ct);
    }
}
