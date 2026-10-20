using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="ILoanQueryService"/> for Catalog's book details (CAT-08) and loan history (G3-09).
/// </summary>
public sealed class LoanQueryService(LibraryDbContext db) : ILoanQueryService
{
    public Task<DateTime?> GetNextDueDateAsync(Guid bookId, CancellationToken ct = default) =>
        (from loan in db.Loans.AsNoTracking()
         join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
         where copy.BookId == bookId && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
         select (DateTime?)loan.DueAtUtc)
        .MinAsync(ct);

    /// <inheritdoc />
    public async Task<PagedResult<LoanDto>> GetLoanHistoryAsync(
        Guid? staffMemberId,
        Guid? bookCopyId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = db.Loans.AsNoTracking().AsQueryable();

        if (staffMemberId.HasValue)
        {
            query = query.Where(l => l.StaffMemberId == staffMemberId.Value);
        }

        if (bookCopyId.HasValue)
        {
            query = query.Where(l => l.BookCopyId == bookCopyId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(l => l.IssuedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoanDto(
                l.Id,
                l.BookCopyId,
                l.StaffMemberId,
                l.IssuedAtUtc,
                l.DueAtUtc,
                (int)(l.DueAtUtc - l.IssuedAtUtc).TotalDays,
                l.Status
            ))
            .ToListAsync(cancellationToken);

        return new PagedResult<LoanDto>(items, totalCount, page, pageSize);
    }
}