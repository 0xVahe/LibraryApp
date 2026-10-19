using System.Globalization;
using FluentValidation;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// CAT-09: books from a CSV file, all or nothing, like the staff import (MEM-09). Every row is checked with the
/// rules of a single book (<see cref="CreateBookRequestValidator"/>); category, publisher and author names must
/// match exactly one existing item (nothing is created silently); ISBNs must be unique in the file and in the
/// catalog (BR-CAT-1); the language must be one of <see cref="CatalogLanguages"/>. The optional cover path is
/// checked by the same validator as on the book form.
/// </summary>
public sealed class BookImportService(
    IBookCsvParser parser,
    IBookService books,
    IBookRepository bookRepository,
    ICategoryRepository categories,
    IPublisherRepository publishers,
    IAuthorRepository authors,
    IValidator<CreateBookRequest> validator) : IBookImportService
{
    /// <summary>Separates several authors in the Authors column.</summary>
    public const char AuthorSeparator = ';';

    public async Task<BookImportResult> ImportAsync(Stream csv, long length, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csv);

        if (length > BookImportLimits.MaximumFileBytes)
            throw BookImportFailure.For(1, "File", "The file must be 1 MB or smaller.");

        var rows = await parser.ParseAsync(csv, ct);

        if (rows.Count == 0)
            throw BookImportFailure.For(2, "File", "The CSV must contain at least one book row.");

        if (rows.Count > BookImportLimits.MaximumRows)
            throw BookImportFailure.For(rows[BookImportLimits.MaximumRows].RowNumber, "File",
                $"A file may contain at most {BookImportLimits.MaximumRows} book rows.");

        // Names → IDs: one query per list, not one per row.
        var categoryIdsByName = GroupIdsByName(await categories.ListAsync(ct), x => x.Name, x => x.Id);
        var publisherIdsByName = GroupIdsByName(await publishers.ListAsync(ct), x => x.Name, x => x.Id);
        var authorIdsByName = GroupIdsByName(await authors.ListAsync(ct), x => x.FullName, x => x.Id);

        // ISBNs already used by active books: one query for the whole file.
        var normalizedIsbnByRow = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            if (IsbnRules.TryNormalize(row.Isbn, out var isbn))
                normalizedIsbnByRow[row.RowNumber] = isbn;
        }
        var existingIsbns = (await bookRepository.GetExistingIsbnsAsync(normalizedIsbnByRow.Values.Distinct().ToArray(), ct))
            .ToHashSet(StringComparer.Ordinal);

        var errors = new List<BookImportError>();
        var firstRowByIsbn = new Dictionary<string, int>(StringComparer.Ordinal);
        var prepared = new List<(BookCsvRow Row, CreateBookRequest Request)>();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var rowErrors = new List<BookImportError>();

            // Publication year: a whole number; the validator checks the range.
            var yearIsNumber = int.TryParse(row.PublicationYear.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var year);
            if (!yearIsNumber)
                rowErrors.Add(new BookImportError(row.RowNumber, "PublicationYear", "Publication year must be a whole number."));

            // Language: only the languages of the collection.
            var language = row.Language.Trim().ToLowerInvariant();
            var languageIsKnown = CatalogLanguages.IsKnown(language);
            if (!languageIsKnown)
                rowErrors.Add(new BookImportError(row.RowNumber, "Language",
                    "Language must be one of: " + string.Join(", ", CatalogLanguages.All.Select(x => x.Code)) + "."));

            // Category (required) and publisher (optional): exactly one existing item with that name.
            var categoryId = FindIdByName(categoryIdsByName, row.Category, "Category", "Category", row.RowNumber, rowErrors);
            if (string.IsNullOrWhiteSpace(row.Category))
                rowErrors.Add(new BookImportError(row.RowNumber, "Category", "Category is required."));

            Guid? publisherId = null;
            if (!string.IsNullOrWhiteSpace(row.Publisher))
                publisherId = FindIdByName(publisherIdsByName, row.Publisher, "Publisher", "Publisher", row.RowNumber, rowErrors);

            // Authors: one or more names separated by ';', each must be an existing author.
            var authorNames = row.Authors
                .Split(AuthorSeparator)
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (authorNames.Count == 0)
                rowErrors.Add(new BookImportError(row.RowNumber, "Authors", "At least one author is required."));

            var authorIds = new List<Guid>();
            foreach (var authorName in authorNames)
            {
                var authorId = FindIdByName(authorIdsByName, authorName, "Authors", "Author", row.RowNumber, rowErrors);
                if (authorId.HasValue)
                    authorIds.Add(authorId.Value);
            }

            // ISBN: unique in the file and among active books (BR-CAT-1). The validator reports an invalid ISBN.
            if (normalizedIsbnByRow.TryGetValue(row.RowNumber, out var normalizedIsbn))
            {
                if (firstRowByIsbn.TryGetValue(normalizedIsbn, out var firstRow))
                    rowErrors.Add(new BookImportError(row.RowNumber, "Isbn", $"ISBN duplicates row {firstRow}."));
                else
                    firstRowByIsbn[normalizedIsbn] = row.RowNumber;

                if (existingIsbns.Contains(normalizedIsbn))
                    rowErrors.Add(new BookImportError(row.RowNumber, "Isbn", CatalogErrors.BookIsbnDuplicate.Message));
            }

            // The rules of a single book. IDs that could not be resolved get a placeholder, because their name error
            // is already reported above.
            var request = new CreateBookRequest(
                row.Title.Trim(),
                row.Isbn.Trim(),
                year,
                language,
                string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                categoryId ?? Guid.NewGuid(),
                string.IsNullOrWhiteSpace(row.Publisher) ? null : publisherId ?? Guid.NewGuid(),
                authorIds.Count > 0 ? authorIds : [Guid.NewGuid()],
                string.IsNullOrWhiteSpace(row.CoverImagePath) ? null : row.CoverImagePath.Trim());

            var validation = await validator.ValidateAsync(request, ct);
            foreach (var failure in validation.Errors)
            {
                if (failure.PropertyName == nameof(CreateBookRequest.PublicationYear) && !yearIsNumber)
                    continue;   // already reported: not a number
                if (failure.PropertyName == nameof(CreateBookRequest.Language) && !languageIsKnown)
                    continue;   // already reported: not one of the collection's languages
                rowErrors.Add(new BookImportError(row.RowNumber, failure.PropertyName, failure.ErrorMessage));
            }

            if (rowErrors.Count == 0)
                prepared.Add((row, request));
            else
                errors.AddRange(rowErrors);
        }

        if (errors.Count > 0)
            throw BookImportFailure.Of(errors);

        var bookIds = await books.CreateBatchAsync(prepared.Select(x => x.Request).ToList(), ct);

        var imported = new List<ImportedBook>(prepared.Count);
        for (var i = 0; i < prepared.Count; i++)
            imported.Add(new ImportedBook(prepared[i].Row.RowNumber, bookIds[i], prepared[i].Request.Title));

        return new BookImportResult(imported.Count, imported);
    }

    /// <summary>Name (trimmed, any case) → the IDs of every item with that name; more than one ID means the name is ambiguous.</summary>
    private static Dictionary<string, List<Guid>> GroupIdsByName<T>(IEnumerable<T> items, Func<T, string> name, Func<T, Guid> id) =>
        items
            .GroupBy(item => name(item).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(id).ToList(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The ID of the one item with this name, or null with a row error when there is none or more than one.</summary>
    private static Guid? FindIdByName(Dictionary<string, List<Guid>> idsByName, string name, string field, string label,
        int rowNumber, List<BookImportError> rowErrors)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return null;

        if (!idsByName.TryGetValue(trimmed, out var ids))
        {
            rowErrors.Add(new BookImportError(rowNumber, field, $"{label} '{trimmed}' was not found."));
            return null;
        }

        if (ids.Count > 1)
        {
            rowErrors.Add(new BookImportError(rowNumber, field, $"{label} name '{trimmed}' is used by more than one {label.ToLowerInvariant()}."));
            return null;
        }

        return ids[0];
    }
}
