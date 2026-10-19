using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>Reads the book CSV behind an Infrastructure adapter (CAT-09).</summary>
public interface IBookCsvParser
{
    /// <summary>
    /// The data rows of the file. A broken file (not UTF-8, missing or unknown column, wrong number of cells)
    /// throws <c>catalog.import_invalid</c> with the line and the reason.
    /// </summary>
    Task<IReadOnlyList<BookCsvRow>> ParseAsync(Stream csv, CancellationToken ct = default);
}
