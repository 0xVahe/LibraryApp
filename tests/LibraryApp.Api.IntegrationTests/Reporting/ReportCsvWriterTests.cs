using System.Reflection;
using System.Text;

namespace LibraryApp.Api.IntegrationTests.Reporting;

public sealed class ReportCsvWriterTests
{
    [Theory]
    [InlineData("Hello, World", "\"Hello, World\"")]
    [InlineData("He said \"Hello\"", "\"He said \"\"Hello\"\"\"")]
    [InlineData("Հայերեն", "\"Հայերեն\"")]
    [InlineData("=SUM(1,2)", "\"'=SUM(1,2)\"")]
    [InlineData("  =SUM(1,2)", "\"'  =SUM(1,2)\"")]
    [InlineData("@SUM(1,2)", "\"'@SUM(1,2)\"")]
    [InlineData("Line1\nLine2", "\"Line1\nLine2\"")]
    public void Csv_writer_escapes_values(string input, string expected)
    {
        string csv = WriteCsv(["Value"], [[input]]);

        Assert.Equal($"\"Value\"\r\n{expected}\r\n", csv);
    }

    [Fact]
    public void Csv_writer_includes_utf8_bom()
    {
        byte[] bytes = WriteBytes(["Name"], [["Հայերեն"]]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
    }

    [Fact]
    public void Csv_writer_rejects_incorrect_column_count()
    {
        var exception = Assert.Throws<TargetInvocationException>(
            () => WriteBytes(["First", "Second"], [["Only one"]]));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    private static string WriteCsv(string[] headers, string?[][] rows)
    {
        byte[] bytes = WriteBytes(headers, rows);

        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    private static byte[] WriteBytes(string[] headers, string?[][] rows)
    {
        var assembly = typeof(LibraryApp.Api.Controllers.Reporting.ReportsController).Assembly;

        var writer = assembly.GetType(
            "LibraryApp.Api.Controllers.Reporting.ReportCsvWriter", true)!;

        var method = writer.GetMethod(
            "Write", BindingFlags.Public | BindingFlags.Static)!;

        return (byte[])method.Invoke(null, [headers, rows])!;
    }
}
