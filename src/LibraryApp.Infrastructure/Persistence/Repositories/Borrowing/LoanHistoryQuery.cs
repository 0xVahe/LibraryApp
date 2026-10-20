using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>BOR-10: an <c>AsNoTracking</c> projection of the Loans table, newest issue first.</summary>
public sealed class LoanHistoryQuery(LibraryDbContext db) : ILoanHistoryQuery
{
    public async Task<PagedResult<LoanHistoryItemDto>> PageAsync(
        Guid? staffMemberId,
        IReadOnlyCollection<Guid>? bookCopyIds,
        LoanStatus? status,
        DateTime? issuedFromUtc,
        DateTime? issuedToUtc,
        PageRequest page,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<Loan> query = db.Loans.AsNoTracking();

        if (staffMemberId is { } staff)
            query = query.Where(loan => loan.StaffMemberId == staff);

        if (bookCopyIds is not null)
            query = query.Where(loan => bookCopyIds.Contains(loan.BookCopyId));

        if (status is { } wanted)
            query = query.Where(loan => loan.Status == wanted);

        if (issuedFromUtc is { } from)
            query = query.Where(loan => loan.IssuedAtUtc >= from);

        if (issuedToUtc is { } to)
            query = query.Where(loan => loan.IssuedAtUtc <= to);

        int total = await query.CountAsync(ct);

        List<LoanHistoryItemDto> items = await query
            .OrderByDescending(loan => loan.IssuedAtUtc)
            .ThenByDescending(loan => loan.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(loan => new LoanHistoryItemDto(
                loan.Id,
                loan.BookCopyId,
                loan.StaffMemberId,
                loan.IssuedAtUtc,
                loan.DueAtUtc,
                loan.ReturnedAtUtc,
                loan.RenewalCount,
                loan.Status))
            .ToListAsync(ct);

        return new PagedResult<LoanHistoryItemDto>(items, total, page.Page, page.PageSize);
    }
}
