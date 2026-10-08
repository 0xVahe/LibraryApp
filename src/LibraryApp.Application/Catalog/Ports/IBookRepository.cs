using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

public interface IBookRepository
{
    Task<Book?> FindAsync(Guid id, CancellationToken ct);

    Task<bool> CategoryExistsAsync(Guid id, CancellationToken ct);

    Task<bool> PublisherExistsAsync(Guid id, CancellationToken ct);

    Task<bool> IsbnExistsAsync(
        string isbn,
        Guid? excludeBookId,
        CancellationToken ct);

    Task<bool> HasCopiesInCirculationAsync(
        Guid bookId,
        CancellationToken ct);

    void Add(Book book);
}