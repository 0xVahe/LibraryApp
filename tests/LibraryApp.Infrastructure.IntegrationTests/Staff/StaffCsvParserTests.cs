using System.Text;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Infrastructure.Staff;

namespace LibraryApp.Infrastructure.IntegrationTests.Staff;

public sealed class StaffCsvParserTests
{
    private readonly StaffCsvParser _parser = new();

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
        for (var i = 0; i <= LibraryApp.Application.Staff.Dto.StaffImportLimits.MaximumRows; i++)
            csv.Append($"E-{i},Anna,Employee,anna{i}@ameriabank.am,IT,Engineer,,Employee\n");

        var error = await Assert.ThrowsAsync<StaffCsvFormatException>(
            () => Parse(csv.ToString()));

        Assert.Equal("File", error.Field);
        Assert.Equal(LibraryApp.Application.Staff.Dto.StaffImportLimits.MaximumRows + 2, error.RowNumber);
    }

    private async Task<IReadOnlyList<LibraryApp.Application.Staff.Dto.StaffImportCsvRow>> Parse(string csv)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await _parser.ParseAsync(stream);
    }
}
