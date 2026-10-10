using LibraryApp.Application.Abstractions.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>
/// Read-only copy lookups behind <see cref="ICatalogService"/> (CAT-07, INT-01). Implemented in Infrastructure.
/// Nothing is tracked: these methods never change data.
/// </summary>
public interface ICatalogCopyQuery
{
    /// <summary>
    /// The Available copy with the smallest barcode, or null when the book has no Available copy,
    /// is soft-deleted or is unknown.
    /// </summary>
    Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct);

    /// <summary>The copy with this barcode, Lost and Withdrawn included, or null. Pass the normalized barcode.</summary>
    Task<BookCopyDto?> GetByBarcodeAsync(string barcode, CancellationToken ct);

    /// <summary>The copies with these ids, ordered by barcode. Unknown ids are left out.</summary>
    Task<IReadOnlyList<BookCopyDto>> GetByIdsAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct);

    /// <summary>Ids of all copies of the book, Lost and Withdrawn included, ordered by barcode.</summary>
    Task<IReadOnlyList<Guid>> GetIdsByBookAsync(Guid bookId, CancellationToken ct);
}
