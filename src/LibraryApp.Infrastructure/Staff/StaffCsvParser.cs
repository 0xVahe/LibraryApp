using System.Text;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Application.Staff.Ports;

namespace LibraryApp.Infrastructure.Staff;

/// <summary>RFC 4180-style CSV reader for the HR staff import format.</summary>
public sealed class StaffCsvParser : IStaffCsvParser
{
    private static readonly string[] RequiredHeaders =
    [
        "EmployeeId", "FirstName", "LastName", "CorporateEmail", "Department", "Position",
        "LineManagerEmail", "Category"
    ];

    public async Task<IReadOnlyList<StaffImportCsvRow>> ParseAsync(Stream csv, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        using var reader = new StreamReader(csv, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024, leaveOpen: true);
        string content;
        try
        {
            content = await reader.ReadToEndAsync(ct);
        }
        catch (DecoderFallbackException ex)
        {
            throw new StaffCsvFormatException(1, "File", $"The file must be valid UTF-8: {ex.Message}");
        }

        var records = ParseRecords(content);
        if (records.Count == 0)
            throw new StaffCsvFormatException(1, "Header", "The CSV header is missing.");

        var headerRecord = records[0];
        var headers = headerRecord.Fields.Select(field => field.Trim()).ToArray();
        var expected = RequiredHeaders.Append("Role").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            if (!expected.Contains(header))
                throw new StaffCsvFormatException(headerRecord.RowNumber, header.Length == 0 ? "Header" : header,
                    $"Unexpected column '{header}'.");
            if (!indexes.TryAdd(header, i))
                throw new StaffCsvFormatException(headerRecord.RowNumber, header, $"Column '{header}' appears more than once.");
        }

        foreach (var required in RequiredHeaders)
            if (!indexes.ContainsKey(required))
                throw new StaffCsvFormatException(headerRecord.RowNumber, required, $"Required column '{required}' is missing.");

        var result = new List<StaffImportCsvRow>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.All(string.IsNullOrWhiteSpace))
                continue;
            if (record.Fields.Count != headers.Length)
                throw new StaffCsvFormatException(record.RowNumber, "Row",
                    $"Expected {headers.Length} values but found {record.Fields.Count}.");

            string Value(string name) => indexes.TryGetValue(name, out var index) ? record.Fields[index].Trim() : string.Empty;
            result.Add(new StaffImportCsvRow(record.RowNumber, Value("EmployeeId"), Value("FirstName"),
                Value("LastName"), Value("CorporateEmail"), Value("Department"), Value("Position"),
                NullIfEmpty(Value("LineManagerEmail")), Value("Category"), NullIfEmpty(Value("Role"))));
            if (result.Count > StaffImportLimits.MaximumRows)
                throw new StaffCsvFormatException(record.RowNumber, "File",
                    $"A file may contain at most {StaffImportLimits.MaximumRows} data rows.");
        }

        return result;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<CsvRecord> ParseRecords(string input)
    {
        var records = new List<CsvRecord>();
        if (input.Length == 0)
            return records;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var quoteClosed = false;
        var fieldStarted = false;
        var recordStartLine = 1;
        var line = 1;

        void FinishField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
            quoteClosed = false;
        }

        void FinishRecord()
        {
            FinishField();
            records.Add(new CsvRecord(recordStartLine, fields));
            fields = [];
            recordStartLine = line + 1;
        }

        for (var i = 0; i < input.Length; i++)
        {
            var character = input[i];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (i + 1 < input.Length && input[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                        quoteClosed = true;
                    }
                }
                else
                {
                    field.Append(character);
                    if (character == '\n')
                        line++;
                }
                continue;
            }

            if (character == '"')
            {
                if (fieldStarted || field.Length > 0)
                    throw new StaffCsvFormatException(line, "Row", "A quote may only start an empty CSV field.");
                inQuotes = true;
                fieldStarted = true;
            }
            else if (character == ',')
                FinishField();
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && i + 1 < input.Length && input[i + 1] == '\n')
                    i++;
                FinishRecord();
                line++;
                recordStartLine = line;
            }
            else
            {
                if (quoteClosed)
                    throw new StaffCsvFormatException(line, "Row", "Only a comma or line break may follow a closing quote.");
                field.Append(character);
                fieldStarted = true;
            }
        }

        if (inQuotes)
            throw new StaffCsvFormatException(recordStartLine, "Row", "A quoted field was not closed.");
        if (fields.Count > 0 || field.Length > 0 || fieldStarted || input[^1] is not ('\r' or '\n'))
        {
            FinishField();
            records.Add(new CsvRecord(recordStartLine, fields));
        }

        return records;
    }

    private sealed record CsvRecord(int RowNumber, List<string> Fields);
}
