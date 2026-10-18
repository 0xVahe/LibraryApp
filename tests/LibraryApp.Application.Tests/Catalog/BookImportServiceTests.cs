using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>CAT-09: the book CSV import checks every row, then saves all books or none.</summary>
public class BookImportServiceTests
{
    private readonly FakeBooks _books = new();
    private readonly FakeAuthors _authors = new();
    private readonly FakeCategories _categories = new();
    private readonly FakePublishers _publishers = new();
    private readonly TestTransaction _transaction = new();
    private readonly TestClock _clock = new(new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
    private readonly RowsParser _parser = new();

    private readonly Category _it = new(Guid.NewGuid(), "IT & Software Engineering");
    private readonly Publisher _wiley = new(Guid.NewGuid(), "Wiley");
    private readonly Author _martin = new(Guid.NewGuid(), "Robert C. Martin");
    private readonly Author _kleppmann = new(Guid.NewGuid(), "Martin Kleppmann");

    public BookImportServiceTests()
    {
        _categories.Categories.Add(_it);
        _books.CategoryIds.Add(_it.Id);
        _publishers.Publishers.Add(_wiley);
        _books.PublisherIds.Add(_wiley.Id);
        _authors.Authors.AddRange([_martin, _kleppmann]);
    }

    private BookImportService Service()
    {
        var books = new BookService(_books, _authors, _transaction, _transaction, _transaction, _clock);
        return new BookImportService(_parser, books, _books, _categories, _publishers, _authors,
            new CreateBookRequestValidator(_clock));
    }

    private static BookCsvRow Row(int rowNumber, string isbn, string title = "Imported Book", string year = "2020",
        string language = "en", string category = "IT & Software Engineering", string? publisher = null,
        string authors = "Robert C. Martin", string? description = null) =>
        new(rowNumber, title, isbn, year, language, description, category, publisher, authors);

    private async Task<IReadOnlyList<BookImportError>> ImportErrors(params BookCsvRow[] rows)
    {
        _parser.Rows = rows;
        var error = await Assert.ThrowsAsync<DomainException>(() => Service().ImportAsync(Stream.Null, 100));
        Assert.Equal(CatalogErrors.ImportInvalid, error.Error);
        return (IReadOnlyList<BookImportError>)error.Extensions["rows"]!;
    }

    [Fact]
    public async Task Valid_rows_create_all_books_in_one_transaction()
    {
        _parser.Rows =
        [
            Row(2, "979-1-00000-001-5", title: "First", publisher: "wiley", language: "EN"),
            Row(3, "9791000000022", title: "Second", authors: " Martin Kleppmann ; Robert C. Martin ; martin kleppmann "),
        ];

        var result = await Service().ImportAsync(Stream.Null, 100);

        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(new[] { 2, 3 }, result.Books.Select(x => x.RowNumber));
        Assert.Equal(result.Books.Select(x => x.BookId), _books.Books.Select(x => x.Id));
        var first = _books.Books[0];
        Assert.Equal("9791000000015", first.Isbn);
        Assert.Equal("en", first.Language);
        Assert.Equal(_wiley.Id, first.PublisherId);
        Assert.Equal(3, _authors.Links.Count);   // 1 + 2: the repeated author counts once
        Assert.Equal(new[] { "catalog:books" }, _transaction.Locks);
        Assert.Equal(1, _books.ExistingIsbnQueries);   // one query for the whole file
    }

    [Fact]
    public async Task Any_invalid_row_saves_nothing_and_every_problem_is_reported()
    {
        var errors = await ImportErrors(
            Row(2, "9791000000015"),
            Row(3, "9791000000022", category: "Unknown Category"),
            Row(4, "9791000000039", authors: "Nobody Known"),
            Row(5, "9791000000046", year: "two thousand"),
            Row(6, "9791000000053", publisher: "Unknown Press"));

        Assert.Empty(_books.Books);
        Assert.Contains(errors, e => e is { RowNumber: 3, Field: "Category", Message: "Category 'Unknown Category' was not found." });
        Assert.Contains(errors, e => e is { RowNumber: 4, Field: "Authors", Message: "Author 'Nobody Known' was not found." });
        Assert.Contains(errors, e => e is { RowNumber: 5, Field: "PublicationYear", Message: "Publication year must be a whole number." });
        Assert.Contains(errors, e => e is { RowNumber: 6, Field: "Publisher", Message: "Publisher 'Unknown Press' was not found." });
        Assert.DoesNotContain(errors, e => e.RowNumber == 2);
        Assert.Single(errors, e => e.RowNumber == 5);   // the year is reported once, not again by the range rule
    }

    [Fact]
    public async Task An_ISBN_twice_in_the_file_or_already_in_the_catalog_is_a_row_error()
    {
        _books.Books.Add(new Book(Guid.NewGuid(), "Existing", "9791000000039", 2019, "en", null, _it.Id, null));

        var errors = await ImportErrors(
            Row(2, "9791000000015"),
            Row(3, "979-1-00000-001-5"),
            Row(4, "9791000000039"));

        Assert.Contains(errors, e => e is { RowNumber: 3, Field: "Isbn", Message: "ISBN duplicates row 2." });
        Assert.Contains(errors, e => e.RowNumber == 4 && e.Field == "Isbn" && e.Message == CatalogErrors.BookIsbnDuplicate.Message);
        Assert.DoesNotContain(errors, e => e.RowNumber == 2);
    }

    [Fact]
    public async Task The_rules_of_a_single_book_apply_to_every_row()
    {
        var errors = await ImportErrors(
            Row(2, "not-an-isbn"),
            Row(3, "9791000000022", title: " "),
            Row(4, "9791000000039", year: "2999"));

        Assert.Contains(errors, e => e is { RowNumber: 2, Field: "Isbn" });
        Assert.Contains(errors, e => e is { RowNumber: 3, Field: "Title" });
        Assert.Contains(errors, e => e is { RowNumber: 4, Field: "PublicationYear" });
    }

    [Fact]
    public async Task A_language_outside_the_collection_is_reported_once()
    {
        var errors = await ImportErrors(Row(2, "9791000000015", language: "de"), Row(3, "9791000000022", language: "english"));

        Assert.Equal("Language must be one of: hy, en, ru.", Assert.Single(errors, e => e.RowNumber == 2).Message);
        Assert.Single(errors, e => e.RowNumber == 3);
    }

    [Fact]
    public async Task A_name_used_by_two_items_is_ambiguous()
    {
        _categories.Categories.Add(new Category(Guid.NewGuid(), "it & software engineering"));

        var errors = await ImportErrors(Row(2, "9791000000015"));

        Assert.Equal("Category name 'IT & Software Engineering' is used by more than one category.", Assert.Single(errors).Message);
    }

    [Fact]
    public async Task Missing_category_or_authors_are_required_errors()
    {
        var errors = await ImportErrors(Row(2, "9791000000015", category: "", authors: " ; "));

        Assert.Contains(errors, e => e is { Field: "Category", Message: "Category is required." });
        Assert.Contains(errors, e => e is { Field: "Authors", Message: "At least one author is required." });
    }

    [Fact]
    public async Task An_empty_file_too_many_rows_or_a_file_over_1_MB_are_refused()
    {
        var empty = await ImportErrors();
        Assert.Equal("The CSV must contain at least one book row.", Assert.Single(empty).Message);

        var tooMany = await ImportErrors(Enumerable.Range(2, BookImportLimits.MaximumRows + 1)
            .Select(n => Row(n, "9791000000015")).ToArray());
        Assert.Equal($"A file may contain at most {BookImportLimits.MaximumRows} book rows.", Assert.Single(tooMany).Message);

        _parser.Rows = [Row(2, "9791000000015")];
        var callsBefore = _parser.Calls;
        var tooBig = await Assert.ThrowsAsync<DomainException>(() =>
            Service().ImportAsync(Stream.Null, BookImportLimits.MaximumFileBytes + 1));
        Assert.Equal(CatalogErrors.ImportInvalid, tooBig.Error);
        Assert.Equal(callsBefore, _parser.Calls);   // refused before the file is read
    }

    private sealed class RowsParser : IBookCsvParser
    {
        public IReadOnlyList<BookCsvRow> Rows { get; set; } = [];
        public int Calls { get; private set; }

        public Task<IReadOnlyList<BookCsvRow>> ParseAsync(Stream csv, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Rows);
        }
    }
}
