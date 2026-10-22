using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="ILoanQueryService"/> for Catalog's book details (CAT-08). Borrowing reads only its own tables: the copies
/// of the book come from Catalog through <see cref="ICatalogService.GetCopyIdsAsync"/>. <c>CatalogService</c> does not
/// depend on this service, so there is no dependency cycle.
/// </summary>
public sealed class LoanQueryService(LibraryDbContext db, ICatalogService catalog) : ILoanQueryService
{
    public async Task<DateTime?> GetNextDueDateAsync(Guid bookId, CancellationToken ct = default)
    {
        IReadOnlyList<Guid> copyIds = await catalog.GetCopyIdsAsync(bookId, ct);
        if (copyIds.Count == 0)
            return null;

        return await db.Loans.AsNoTracking()
            .Where(loan => copyIds.Contains(loan.BookCopyId)
                           && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue))
            .Select(loan => (DateTime?)loan.DueAtUtc)
            .MinAsync(ct);
    }
}
