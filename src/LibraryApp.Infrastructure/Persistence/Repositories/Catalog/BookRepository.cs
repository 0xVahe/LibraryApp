using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class BookRepository(LibraryDbContext db) : IBookRepository
{
    public Task<Book?> FindAsync(Guid id, CancellationToken ct) =>
        db.Books.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> CategoryExistsAsync(Guid id, CancellationToken ct) =>
        db.Categories.AnyAsync(x => x.Id == id, ct);

    public Task<bool> PublisherExistsAsync(Guid id, CancellationToken ct) =>
        db.Publishers.AnyAsync(x => x.Id == id, ct);

    public Task<bool> IsbnExistsAsync(
        string isbn,
        Guid? excludeBookId,
        CancellationToken ct) =>
        db.Books.AnyAsync(
            x => !x.IsDeleted
                 && x.Isbn == isbn
                 && (!excludeBookId.HasValue || x.Id != excludeBookId.Value),
            ct);

    public async Task<IReadOnlyList<BookCopy>> GetCopiesForUpdateAsync(
        Guid bookId,
        CancellationToken ct)
    {
        return await db.BookCopies
            .Where(x => x.BookId == bookId)
            .ToListAsync(ct);
    }

    public void Add(Book book) =>
        db.Books.Add(book);
}