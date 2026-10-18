using System.Text;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;

namespace LibraryApp.Infrastructure.IntegrationTests.Catalog;

/// <summary>CAT-09: reading the book CSV (no database).</summary>
public sealed class BookCsvParserTests
{
    private const string Header = "Title,Isbn,PublicationYear,Language,Category,Authors,Publisher,Description";
    private readonly BookCsvParser _parser = new();

    private Task<IReadOnlyList<BookCsvRow>> Parse(string csv, bool bom = false)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        if (bom)
            bytes = Encoding.UTF8.GetPreamble().Concat(bytes).ToArray();
        return _parser.ParseAsync(new MemoryStream(bytes));
    }

    private async Task<BookImportError> ParseError(string csv)
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Parse(csv));
        Assert.Equal(CatalogErrors.ImportInvalid, error.Error);
        return Assert.Single((IReadOnlyList<BookImportError>)error.Extensions["rows"]!);
    }

    [Fact]
    public async Task Quoted_cells_keep_their_commas_and_quotes()
    {
        var rows = await Parse(Header + "\r\n" +
            "\"Data, Systems and Scale\",9791000000022,2021,en,\"Risk, Compliance & AML\",A; B,\"O\"\"Reilly\",\r\n");

        var row = Assert.Single(rows);
        Assert.Equal("Data, Systems and Scale", row.Title);
        Assert.Equal("Risk, Compliance & AML", row.Category);
        Assert.Equal("O\"Reilly", row.Publisher);
        Assert.Equal("A; B", row.Authors);
        Assert.Null(row.Description);
    }

    [Fact]
    public async Task Columns_may_come_in_any_order_and_case_and_optional_ones_may_be_absent()
    {
        var rows = await Parse("authors,TITLE,isbn,Category,Language,PublicationYear\nRobert C. Martin,Clean,9791000000015,IT,en,2020\n");

        var row = Assert.Single(rows);
        Assert.Equal("Clean", row.Title);
        Assert.Equal("Robert C. Martin", row.Authors);
        Assert.Null(row.Publisher);
    }

    [Fact]
    public async Task A_BOM_and_empty_lines_are_accepted_and_row_numbers_are_file_lines()
    {
        var rows = await Parse(Header + "\r\n\r\nA,9791000000015,2020,en,IT,X,,\r\n\"Two\r\nlines\",9791000000022,2020,en,IT,X,,\r\nC,9791000000039,2020,en,IT,X,,", bom: true);

        Assert.Equal(new[] { 3, 4, 6 }, rows.Select(x => x.RowNumber));
        Assert.Equal("Two\r\nlines", rows[1].Title);
    }

    [Fact]
    public async Task Armenian_and_Russian_text_is_read_as_UTF8()
    {
        var rows = await Parse(Header + "\nԱնուշ,9791000000015,1892,hy,General Reading,Հովհաննես Թումանյան,,\nПиши,9791000000022,2016,ru,Soft Skills,Максим Ильяхов,,\n");

        Assert.Equal(new[] { "Անուշ", "Пиши" }, rows.Select(x => x.Title));
    }

    [Fact]
    public async Task A_file_that_is_not_UTF8_is_refused()
    {
        var bytes = Encoding.UTF8.GetBytes(Header + "\n").Concat(new byte[] { 0xC0, 0xC1, 0x0A }).ToArray();
        var error = await Assert.ThrowsAsync<DomainException>(() => _parser.ParseAsync(new MemoryStream(bytes)));

        Assert.Equal(CatalogErrors.ImportInvalid, error.Error);
    }

    [Fact]
    public async Task Header_problems_are_reported_on_line_1()
    {
        Assert.Equal("The CSV header line is missing.", (await ParseError("")).Message);
        Assert.Equal("Required column 'Authors' is missing.", (await ParseError("Title,Isbn,PublicationYear,Language,Category\n")).Message);
        Assert.StartsWith("Unexpected column 'Price'.", (await ParseError(Header + ",Price\n")).Message);
        Assert.Equal("Column 'Title' appears more than once.", (await ParseError(Header + ",title\n")).Message);
    }

    [Fact]
    public async Task A_row_with_the_wrong_number_of_cells_names_its_line()
    {
        var error = await ParseError(Header + "\nA,9791000000015,2020,en,IT,X,,\nB,9791000000022,2020\n");

        Assert.Equal(3, error.RowNumber);
        Assert.Equal("Row", error.Field);
    }

    [Fact]
    public async Task The_sample_file_reads_and_uses_only_seeded_names()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "samples", "book-import.csv");
        await using var file = File.OpenRead(path);

        var rows = await _parser.ParseAsync(file);

        Assert.NotEmpty(rows);
        var categories = CatalogSeedData.Categories.Select(x => x.Name).ToHashSet();
        var authors = CatalogSeedData.Authors.Select(x => x.Name).ToHashSet();
        var publishers = CatalogSeedData.Publishers.Select(x => x.Name).ToHashSet();
        foreach (var row in rows)
        {
            Assert.Contains(row.Category, categories);
            Assert.All(row.Authors.Split(';'), name => Assert.Contains(name.Trim(), authors));
            if (row.Publisher is not null)
                Assert.Contains(row.Publisher, publishers);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LibraryApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("LibraryApp.sln was not found above the test folder.");
    }
}
