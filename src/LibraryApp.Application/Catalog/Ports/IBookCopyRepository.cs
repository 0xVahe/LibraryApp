using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>Database access for managing copies (CAT-04). Implemented in Infrastructure.</summary>
public interface IBookCopyRepository
{
    /// <summary>Whether the book exists and is not soft-deleted.</summary>
    Task<BookState> GetBookStateAsync(Guid bookId, CancellationToken ct);

    /// <summary>True if any copy, retired ones included, has this barcode (BR-CAT-2). Pass the normalized barcode.</summary>
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct);

    /// <summary>All copies of the book, retired ones included, ordered by barcode. Read only.</summary>
    Task<IReadOnlyList<BookCopy>> GetByBookAsync(Guid bookId, CancellationToken ct);

    /// <summary>Tracked: changes are saved by the unit of work. Also finds a copy added in the same unit of work.</summary>
    Task<BookCopy?> GetByIdAsync(Guid copyId, CancellationToken ct);

    /// <summary>Saved by the unit of work.</summary>
    void Add(BookCopy copy);
}

/// <summary>State of a book as seen by copy management.</summary>
public enum BookState
{
    NotFound = 1,
    Active = 2,
    Deleted = 3,
}
