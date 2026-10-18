using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// The answer to an invalid book CSV (CAT-09): 400 <c>catalog.import_invalid</c> with every problem in the
/// <c>rows</c> extension of the ProblemDetails, the same shape as the staff import (MEM-09).
/// </summary>
public static class BookImportFailure
{
    public static DomainException Of(IReadOnlyList<BookImportError> rows) =>
        new(CatalogErrors.ImportInvalid, extensions: new Dictionary<string, object?> { ["rows"] = rows });

    public static DomainException For(int rowNumber, string field, string message) =>
        Of([new BookImportError(rowNumber, field, message)]);
}
