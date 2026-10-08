using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class BookCopyRepository(LibraryDbContext db) : IBookCopyRepository
{
    public async Task<BookState> GetBookStateAsync(Guid bookId, CancellationToken ct)
    {
        bool? isDeleted = await db.Books
            .AsNoTracking()
            .Where(book => book.Id == bookId)
            .Select(book => (bool?)book.IsDeleted)
            .SingleOrDefaultAsync(ct);

        return isDeleted switch
        {
            null => BookState.NotFound,
            true => BookState.Deleted,
            false => BookState.Active,
        };
    }

    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct) =>
        db.BookCopies.AnyAsync(copy => copy.Barcode == barcode, ct);

    public async Task<IReadOnlyList<BookCopy>> GetByBookAsync(Guid bookId, CancellationToken ct) =>
        await db.BookCopies
            .AsNoTracking()
            .Where(copy => copy.BookId == bookId)
            .OrderBy(copy => copy.Barcode)
            .ToListAsync(ct);

    public Task<BookCopy?> GetByIdAsync(Guid copyId, CancellationToken ct) =>
        db.BookCopies.SingleOrDefaultAsync(copy => copy.Id == copyId, ct);

    public void Add(BookCopy copy) => db.BookCopies.Add(copy);
}
