namespace LibraryApp.Application.Catalog.Dto;

/// <summary>
/// One data row of the book CSV, as text (CAT-09). <paramref name="RowNumber"/> is the line where the row begins;
/// the header is line 1. Empty optional cells are null.
/// </summary>
/// <param name="CoverImagePath">Optional path inside the cover storage, checked like on the book form (CAT-01).</param>
public sealed record BookCsvRow(
    int RowNumber,
    string Title,
    string Isbn,
    string PublicationYear,
    string Language,
    string? Description,
    string Category,
    string? Publisher,
    string Authors,
    string? CoverImagePath = null);

/// <summary>One problem in the CSV: the line, the column and what is wrong.</summary>
public sealed record BookImportError(int RowNumber, string Field, string Message);

/// <summary>A book created by the import, with the CSV line it came from.</summary>
public sealed record ImportedBook(int RowNumber, Guid BookId, string Title);

/// <summary>The answer to a successful import: every row created one book.</summary>
public sealed record BookImportResult(int CreatedCount, IReadOnlyList<ImportedBook> Books);
