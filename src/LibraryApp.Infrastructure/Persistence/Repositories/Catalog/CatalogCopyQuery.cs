using System.Linq.Expressions;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

/// <summary>Read-only copy lookups for <see cref="ICatalogService"/> (CAT-07, INT-01). Every query is one SQL statement.</summary>
public sealed class CatalogCopyQuery(LibraryDbContext db) : ICatalogCopyQuery
{
    private static readonly Expression<Func<BookCopy, BookCopyDto>> ToDto = copy =>
        new BookCopyDto(copy.Id, copy.BookId, copy.Barcode, copy.Status, copy.Condition, copy.ShelfLocation);

    public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct) =>
        db.BookCopies
            .AsNoTracking()
            .Where(copy => copy.BookId == bookId
                && copy.Status == BookCopyStatus.Available
                && db.Books.Any(book => book.Id == bookId && !book.IsDeleted))
            .OrderBy(copy => copy.Barcode)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct);

    public Task<BookCopyDto?> GetByBarcodeAsync(string barcode, CancellationToken ct) =>
        db.BookCopies
            .AsNoTracking()
            .Where(copy => copy.Barcode == barcode)
            .Select(ToDto)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BookCopyDto>> GetByIdsAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct) =>
        await db.BookCopies
            .AsNoTracking()
            .Where(copy => copyIds.Contains(copy.Id))
            .OrderBy(copy => copy.Barcode)
            .Select(ToDto)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetIdsByBookAsync(Guid bookId, CancellationToken ct) =>
        await db.BookCopies
            .AsNoTracking()
            .Where(copy => copy.BookId == bookId)
            .OrderBy(copy => copy.Barcode)
            .Select(copy => copy.Id)
            .ToListAsync(ct);
}
