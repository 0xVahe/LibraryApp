using System.Text;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using Microsoft.VisualBasic.FileIO;

namespace LibraryApp.Infrastructure.Catalog;

/// <summary>
/// Reads the book CSV (CAT-09) with <see cref="TextFieldParser"/> from .NET itself, so no package is needed:
/// comma separated, a header line first, quotes around cells that contain commas, UTF-8 (with or without BOM).
/// Columns may come in any order and any case; Description, Publisher and CoverImagePath are optional.
/// </summary>
public sealed class BookCsvParser : IBookCsvParser
{
    private static readonly string[] RequiredColumns = ["Title", "Isbn", "PublicationYear", "Language", "Category", "Authors"];
    private static readonly string[] OptionalColumns = ["Description", "Publisher", "CoverImagePath"];

    public async Task<IReadOnlyList<BookCsvRow>> ParseAsync(Stream csv, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csv);

        // Read the whole file first (at most 1 MB): invalid UTF-8 is reported once, before any row.
        string content;
        using (var reader = new StreamReader(csv, new UTF8Encoding(false, throwOnInvalidBytes: true),
                   detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            try
            {
                content = await reader.ReadToEndAsync(ct);
            }
            catch (DecoderFallbackException)
            {
                throw BookImportFailure.For(1, "File", "The file must be UTF-8 text (save it as \"CSV UTF-8\").");
            }
        }

        using var parser = new TextFieldParser(new StringReader(content))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true,
        };
        parser.SetDelimiters(",");

        var columnIndexes = ReadHeader(parser);
        var totalLines = CountLines(content);
        var rows = new List<BookCsvRow>();

        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            string[]? cells;
            try
            {
                cells = parser.ReadFields();
            }
            catch (MalformedLineException ex)
            {
                throw BookImportFailure.For((int)ex.LineNumber, "Row", "The row has a quote that is not closed or not escaped.");
            }

            if (cells is null || cells.All(string.IsNullOrWhiteSpace))
                continue;

            // The parser skips empty lines, so the row number is counted back from the line after the row:
            // the last line of the row, minus the line breaks inside its quoted cells.
            var lastLine = parser.LineNumber == -1 ? totalLines : (int)parser.LineNumber - 1;
            var rowNumber = lastLine - cells.Sum(cell => cell.Count(ch => ch == '\n'));

            if (cells.Length != columnIndexes.Count)
                throw BookImportFailure.For(rowNumber, "Row",
                    $"The row has {cells.Length} cells; the header has {columnIndexes.Count} columns.");

            rows.Add(new BookCsvRow(
                rowNumber,
                Title: Cell(cells, columnIndexes, "Title") ?? "",
                Isbn: Cell(cells, columnIndexes, "Isbn") ?? "",
                PublicationYear: Cell(cells, columnIndexes, "PublicationYear") ?? "",
                Language: Cell(cells, columnIndexes, "Language") ?? "",
                Description: Cell(cells, columnIndexes, "Description"),
                Category: Cell(cells, columnIndexes, "Category") ?? "",
                Publisher: Cell(cells, columnIndexes, "Publisher"),
                Authors: Cell(cells, columnIndexes, "Authors") ?? "",
                CoverImagePath: Cell(cells, columnIndexes, "CoverImagePath")));
        }

        return rows;
    }

    /// <summary>Column name → cell index. Unknown, repeated or missing required columns stop the import.</summary>
    private static Dictionary<string, int> ReadHeader(TextFieldParser parser)
    {
        string[]? header;
        try
        {
            header = parser.EndOfData ? null : parser.ReadFields();
        }
        catch (MalformedLineException)
        {
            header = null;
        }

        if (header is null || header.All(string.IsNullOrWhiteSpace))
            throw BookImportFailure.For(1, "Header", "The CSV header line is missing.");

        var known = RequiredColumns.Concat(OptionalColumns).ToArray();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Length; i++)
        {
            var column = header[i].Trim();
            var knownName = known.FirstOrDefault(name => string.Equals(name, column, StringComparison.OrdinalIgnoreCase));
            if (knownName is null)
                throw BookImportFailure.For(1, column.Length == 0 ? "Header" : column,
                    $"Unexpected column '{column}'. Columns: {string.Join(", ", known)}.");
            if (!indexes.TryAdd(knownName, i))
                throw BookImportFailure.For(1, knownName, $"Column '{knownName}' appears more than once.");
        }

        foreach (var required in RequiredColumns)
        {
            if (!indexes.ContainsKey(required))
                throw BookImportFailure.For(1, required, $"Required column '{required}' is missing.");
        }

        return indexes;
    }

    /// <summary>Number of lines in the text; a last line without a line break counts too.</summary>
    private static int CountLines(string content)
    {
        var lineBreaks = content.Count(ch => ch == '\n');
        return content.Length > 0 && !content.EndsWith('\n') ? lineBreaks + 1 : lineBreaks;
    }

    /// <summary>The cell of a column; null when the column is absent or the cell is empty.</summary>
    private static string? Cell(string[] cells, Dictionary<string, int> indexes, string column) =>
        indexes.TryGetValue(column, out var index) && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index].Trim() : null;
}
