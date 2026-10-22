using System.Globalization;
using System.Text;

namespace LibraryApp.Api.Controllers.Reporting;

internal static class ReportCsvWriter
{
    public static byte[] Write(string[] headers, IEnumerable<string?[]> rows)
    {
        var csv = new StringBuilder();
        AppendRow(csv, headers);
        foreach (string?[] row in rows)
        {
            if (row.Length != headers.Length)
                throw new ArgumentException("CSV row length must match the header length.", nameof(rows));
            AppendRow(csv, row);
        }

        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public static string Value<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> cells)
    {
        csv.AppendJoin(',', cells.Select(Escape));
        csv.Append("\r\n");
    }


    private static string Escape(string? value)
    {
        string text = value ?? string.Empty;
        string normalized = text.TrimStart(' ');

        if (normalized.Length > 0 &&
            "=+-@\t\r\n".Contains(normalized[0]))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}