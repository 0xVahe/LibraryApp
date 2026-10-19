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

    /// <summary>Which of these ISBNs (13 digits) active books already have; one query for the whole list (CAT-09).</summary>
    Task<IReadOnlyCollection<string>> GetExistingIsbnsAsync(
        IReadOnlyCollection<string> isbns,
        CancellationToken ct);

    Task<IReadOnlyList<BookCopy>> GetCopiesForUpdateAsync(
        Guid bookId,
        CancellationToken ct);

    void Add(Book book);
}
