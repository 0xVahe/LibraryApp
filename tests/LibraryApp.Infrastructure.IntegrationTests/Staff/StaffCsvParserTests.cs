using System.Text;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Application.Staff.Options;
using LibraryApp.Infrastructure.Staff;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Staff;

public sealed class StaffCsvParserTests
{
    private const int MaxRows = 2;
    private readonly StaffCsvParser _parser = new(Options.Create(new StaffOptions
    {
        Import = new() { MaxRows = MaxRows, MaxFileBytes = 4096 }
    }));

    [Fact]
    public async Task Parses_quotes_commas_and_escaped_quotes()
    {
        const string csv = "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category,Role\r\n" +
                           "E-1,\"Mary, Jane\",\"O\"\"Brien\",mary@ameriabank.am,\"IT, Security\",Engineer,,Employee,\r\n";

        var rows = await Parse(csv);

        var row = Assert.Single(rows);
        Assert.Equal("Mary, Jane", row.FirstName);
        Assert.Equal("O\"Brien", row.LastName);
        Assert.Equal("IT, Security", row.Department);
        Assert.Null(row.LineManagerEmail);
        Assert.Null(row.Role);
    }

    [Fact]
    public async Task Accepts_utf8_bom_and_skips_empty_lines()
    {
        const string csv = "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category\r\n" +
                           "\r\nE-1,Anna,Petrosyan,anna@ameriabank.am,IT,Engineer,,Employee\r\n\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        await using var stream = new MemoryStream(bytes);

        var rows = await _parser.ParseAsync(stream);

        var row = Assert.Single(rows);
        Assert.Equal(3, row.RowNumber);
    }

    [Fact]
    public async Task Rejects_missing_required_header_with_field_name()
    {
        const string csv = "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail\n";

        var error = await Assert.ThrowsAsync<StaffCsvFormatException>(() => Parse(csv));

        Assert.Equal("Category", error.Field);
        Assert.Equal(1, error.RowNumber);
    }

    [Fact]
    public async Task Rejects_rows_over_the_import_limit()
    {
        var csv = new StringBuilder("EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category\n");
        for (var i = 0; i <= MaxRows; i++)
            csv.Append($"E-{i},Anna,Employee,anna{i}@ameriabank.am,IT,Engineer,,Employee\n");

        var error = await Assert.ThrowsAsync<StaffCsvFormatException>(
            () => Parse(csv.ToString()));

        Assert.Equal("File", error.Field);
        Assert.Equal(MaxRows + 2, error.RowNumber);
    }

    [Fact]
    public async Task Accepts_exactly_the_configured_row_limit()
    {
        const string csv = "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category\n" +
                           "E-1,Anna,Employee,anna@ameriabank.am,IT,Engineer,,Employee\n" +
                           "E-2,Mary,Employee,mary@ameriabank.am,IT,Engineer,,Employee\n";
        Assert.Equal(MaxRows, (await Parse(csv)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Counts_actual_bytes_for_seekable_and_nonseekable_streams(bool nonseekable)
    {
        const string csv = "EmployeeId,FirstName,LastName,CorporateEmail,Department,Position,LineManagerEmail,Category\n" +
                           "E-1,Աննա,Employee,anna@ameriabank.am,IT,Engineer,,Employee\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var parser = new StaffCsvParser(Options.Create(new StaffOptions
        {
            Import = new() { MaxRows = 2, MaxFileBytes = bytes.Length }
        }));
        using Stream exact = nonseekable ? new NonSeekableStream(bytes) : new MemoryStream(bytes);
        Assert.Single(await parser.ParseAsync(exact));

        using Stream over = nonseekable ? new NonSeekableStream([.. bytes, (byte)' ']) : new MemoryStream([.. bytes, (byte)' ']);
        var error = await Assert.ThrowsAsync<StaffCsvFormatException>(() => parser.ParseAsync(over));
        Assert.Equal("File", error.Field);
        Assert.Contains($"{bytes.Length} bytes", error.Message);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }

    private async Task<IReadOnlyList<LibraryApp.Application.Staff.Dto.StaffImportCsvRow>> Parse(string csv)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await _parser.ParseAsync(stream);
    }
}
