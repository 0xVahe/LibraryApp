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

    // FindAsync looks at the tracked copies first, so a copy added in the same unit of work (not saved yet) is found
    // too: Borrowing reserves a new copy through SetCopyStatusAsync before the transaction is saved (BQ-G1-10).
    public async Task<BookCopy?> GetByIdAsync(Guid copyId, CancellationToken ct) =>
        await db.BookCopies.FindAsync(new object[] { copyId }, ct);

    public void Add(BookCopy copy) => db.BookCopies.Add(copy);
}
