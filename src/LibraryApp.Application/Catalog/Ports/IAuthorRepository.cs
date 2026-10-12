using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

public interface IAuthorRepository
{
    Task<IReadOnlyList<AuthorDto>> ListAsync(CancellationToken ct);
    Task<Author?> FindAsync(Guid id, CancellationToken ct);
    void Add(Author author);
    void Remove(Author author);
    Task<bool> IsInUseAsync(Guid id, CancellationToken ct);
    Task<bool> BookExistsAsync(Guid bookId, CancellationToken ct);
    Task<int> CountAuthorsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<BookAuthor>> GetBookAuthorsAsync(Guid bookId, CancellationToken ct);
    void AddLink(BookAuthor link);
    void RemoveLink(BookAuthor link);
}
