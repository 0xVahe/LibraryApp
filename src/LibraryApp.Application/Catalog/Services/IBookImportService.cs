using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Bulk import of books from a CSV file (CAT-09).</summary>
public interface IBookImportService
{
    /// <summary>
    /// Checks every row first. If any row is invalid, nothing is saved and <c>catalog.import_invalid</c> lists every
    /// problem; otherwise all books are created in one transaction.
    /// </summary>
    /// <param name="csv">The file content.</param>
    /// <param name="length">The file size in bytes, checked before the file is read.</param>
    /// <param name="ct">Cancellation.</param>
    Task<BookImportResult> ImportAsync(Stream csv, long length, CancellationToken ct = default);
}
