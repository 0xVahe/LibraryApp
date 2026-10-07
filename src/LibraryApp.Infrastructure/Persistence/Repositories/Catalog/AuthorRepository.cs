using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class AuthorRepository(LibraryDbContext db) : IAuthorRepository
{
    public async Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct) =>
        await db.Authors.AsNoTracking().OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Select(x => new AuthorDto(x.Id, x.FullName)).ToListAsync(ct);

    public Task<Author?> FindAsync(Guid id, CancellationToken ct) =>
        db.Authors.SingleOrDefaultAsync(x => x.Id == id, ct);

    public void Add(Author author) => db.Authors.Add(author);
    public void Remove(Author author) => db.Authors.Remove(author);

    public Task<bool> IsInUseAsync(Guid id, CancellationToken ct) =>
        db.BookAuthors.AnyAsync(x => x.AuthorId == id, ct);

    public Task<bool> BookExistsAsync(Guid bookId, CancellationToken ct) =>
        db.Books.AnyAsync(x => x.Id == bookId && !x.IsDeleted, ct);

    public Task<int> CountAuthorsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        db.Authors.CountAsync(x => ids.Contains(x.Id), ct);

    public async Task<IReadOnlyList<BookAuthor>> GetBookAuthorsAsync(Guid bookId, CancellationToken ct) =>
        await db.BookAuthors.Where(x => x.BookId == bookId).ToListAsync(ct);

    public void AddLink(BookAuthor link) => db.BookAuthors.Add(link);
    public void RemoveLink(BookAuthor link) => db.BookAuthors.Remove(link);
}
