using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Catalog;

/// <summary>
/// A real SQL Server database with a small catalog for the search tests (CAT-05). The data is written once;
/// the tests only read it.
/// </summary>
public sealed class BookSearchDatabase : IAsyncLifetime
{
    public static readonly Guid RiskCategoryId = new("c1000000-0000-0000-0000-000000000001");
    public static readonly Guid AmlSubcategoryId = new("c1000000-0000-0000-0000-000000000002");
    public static readonly Guid ItCategoryId = new("c1000000-0000-0000-0000-000000000003");
    public static readonly Guid PagingCategoryId = new("c1000000-0000-0000-0000-000000000004");

    public static readonly Guid RiskBasicsId = new("b1000000-0000-0000-0000-000000000001");       // Risk, 1 Available + Lost + Withdrawn
    public static readonly Guid AmlEssentialsId = new("b1000000-0000-0000-0000-000000000002");    // AML subcategory, 1 OnLoan
    public static readonly Guid RefactoringId = new("b1000000-0000-0000-0000-000000000003");      // IT, no copies
    public static readonly Guid ArmenianBankingId = new("b1000000-0000-0000-0000-000000000004");  // Risk, language hy
    public static readonly Guid DeletedBookId = new("b1000000-0000-0000-0000-000000000005");      // Risk, soft-deleted

    public const int PagingBookCount = 22;

    public string ConnectionString { get; } = TestDatabase.NewConnectionString();

    public LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(ConnectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var risk = new Category(RiskCategoryId, "Risk, Compliance & AML");
        var aml = new Category(AmlSubcategoryId, "AML Basics");
        aml.SetParent(risk);
        db.Categories.AddRange(risk, aml,
            new Category(ItCategoryId, "IT & Software Engineering"),
            new Category(PagingCategoryId, "Paging"));

        var anna = new Author(Guid.NewGuid(), "Anna Petrosyan");
        var fowler = new Author(Guid.NewGuid(), "Martin Fowler");
        db.Authors.AddRange(anna, fowler);

        AddBook(db, RiskBasicsId, "Risk Management Basics", "9780000000001", "en", RiskCategoryId, anna.Id);
        AddBook(db, AmlEssentialsId, "Anti-Money Laundering Essentials", "9780000000002", "en", AmlSubcategoryId, anna.Id);
        AddBook(db, RefactoringId, "Refactoring", "9780134757599", "en", ItCategoryId, fowler.Id);
        AddBook(db, ArmenianBankingId, "Risk in Armenian Banking", "9780000000004", "hy", RiskCategoryId, anna.Id);
        AddBook(db, DeletedBookId, "Risk Deleted Edition", "9780000000005", "en", RiskCategoryId, anna.Id)
            .Delete(DateTime.UtcNow);

        for (var i = 1; i <= PagingBookCount; i++)
            AddBook(db, Guid.NewGuid(), $"Paging Book {i:00}", $"97810000000{i:00}", "en", PagingCategoryId, fowler.Id);

        db.BookCopies.AddRange(
            Copy(RiskBasicsId, "RB-1", BookCopyStatus.Available),
            Copy(RiskBasicsId, "RB-2", BookCopyStatus.Lost),
            Copy(RiskBasicsId, "RB-3", BookCopyStatus.Withdrawn),
            Copy(AmlEssentialsId, "AML-1", BookCopyStatus.OnLoan),
            Copy(DeletedBookId, "DEL-1", BookCopyStatus.Available));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static Book AddBook(LibraryDbContext db, Guid id, string title, string isbn, string language, Guid categoryId, Guid authorId)
    {
        var book = new Book(id, title, isbn, 2020, language, null, categoryId, null);
        db.Books.Add(book);
        db.BookAuthors.Add(new BookAuthor(id, authorId));
        return book;
    }

    private static BookCopy Copy(Guid bookId, string barcode, BookCopyStatus status)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, barcode, BookCopyCondition.Good, null);
        if (status != BookCopyStatus.Available)
            copy.ChangeStatus(status);
        return copy;
    }
}

public class BookSearchQueryTests(BookSearchDatabase database) : IClassFixture<BookSearchDatabase>
{
    [Fact]
    public async Task Finds_a_book_by_part_of_the_title_ignoring_case()
    {
        var result = await Search(Filter(text: "risk management"));

        Assert.Equal(new[] { BookSearchDatabase.RiskBasicsId }, Ids(result));
    }

    [Fact]
    public async Task Finds_a_book_by_author_name()
    {
        var result = await Search(Filter(text: "Fowler", categoryId: BookSearchDatabase.ItCategoryId));

        Assert.Equal(new[] { BookSearchDatabase.RefactoringId }, Ids(result));
    }

    [Theory]
    [InlineData("978-0-13-475759-9")]
    [InlineData("9780134757599")]
    [InlineData("475759")]
    public async Task Finds_a_book_by_isbn_with_or_without_hyphens(string text)
    {
        var result = await Search(Filter(text: text));

        Assert.Equal(new[] { BookSearchDatabase.RefactoringId }, Ids(result));
    }

    [Fact]
    public async Task Deleted_books_are_never_returned()
    {
        var byTitle = await Search(Filter(text: "Deleted"));
        var byCategory = await Search(Filter(categoryId: BookSearchDatabase.RiskCategoryId));

        Assert.Empty(byTitle.Items);
        Assert.Equal(0, byTitle.TotalCount);
        Assert.DoesNotContain(BookSearchDatabase.DeletedBookId, Ids(byCategory));
    }

    [Fact]
    public async Task A_top_level_category_includes_the_books_of_its_subcategories()
    {
        var result = await Search(Filter(categoryId: BookSearchDatabase.RiskCategoryId));

        Assert.Equal(
            new[] { BookSearchDatabase.AmlEssentialsId, BookSearchDatabase.ArmenianBankingId, BookSearchDatabase.RiskBasicsId }.Order(),
            Ids(result).Order());
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task A_subcategory_returns_only_its_own_books()
    {
        var result = await Search(Filter(categoryId: BookSearchDatabase.AmlSubcategoryId));

        Assert.Equal(new[] { BookSearchDatabase.AmlEssentialsId }, Ids(result));
    }

    [Fact]
    public async Task Filters_by_language()
    {
        var result = await Search(Filter(categoryId: BookSearchDatabase.RiskCategoryId, language: "hy"));

        Assert.Equal(new[] { BookSearchDatabase.ArmenianBankingId }, Ids(result));
    }

    [Fact]
    public async Task Filters_by_availability()
    {
        var available = await Search(Filter(categoryId: BookSearchDatabase.RiskCategoryId, available: true));
        var notAvailable = await Search(Filter(categoryId: BookSearchDatabase.RiskCategoryId, available: false));

        Assert.Equal(new[] { BookSearchDatabase.RiskBasicsId }, Ids(available));
        Assert.Equal(
            new[] { BookSearchDatabase.AmlEssentialsId, BookSearchDatabase.ArmenianBankingId }.Order(),
            Ids(notAvailable).Order());
    }

    [Fact]
    public async Task Copy_counts_leave_out_lost_and_withdrawn_copies()
    {
        var result = await Search(Filter(categoryId: BookSearchDatabase.RiskCategoryId));

        var riskBasics = result.Items.Single(b => b.Id == BookSearchDatabase.RiskBasicsId);
        var aml = result.Items.Single(b => b.Id == BookSearchDatabase.AmlEssentialsId);

        Assert.Equal((1, 1), (riskBasics.AvailableCopies, riskBasics.TotalCopies));   
        Assert.Equal((0, 1), (aml.AvailableCopies, aml.TotalCopies));                
    }

    [Fact]
    public async Task A_result_has_isbn_year_authors_category_and_language()
    {
        var book = (await Search(Filter(text: "risk management"))).Items.Single();

        Assert.Equal("Risk Management Basics", book.Title);
        Assert.Equal("9780000000001", book.Isbn);
        Assert.Equal(2020, book.PublicationYear);
        Assert.Equal(new[] { "Anna Petrosyan" }, book.Authors);
        Assert.Equal("Risk, Compliance & AML", book.Category);
        Assert.Equal("en", book.Language);
    }

    [Fact]
    public async Task Pages_are_cut_by_title_with_the_total_count()
    {
        var filter = Filter(categoryId: BookSearchDatabase.PagingCategoryId);

        var first = await Search(filter, new PageRequest(1, 20));
        var second = await Search(filter, new PageRequest(2, 20));

        Assert.Equal(20, first.Items.Count);
        Assert.Equal(BookSearchDatabase.PagingBookCount, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal("Paging Book 01", first.Items[0].Title);
        Assert.Equal(new[] { "Paging Book 21", "Paging Book 22" }, second.Items.Select(b => b.Title));
    }

    private async Task<PagedResult<BookSearchResultDto>> Search(BookSearchFilter filter, PageRequest? paging = null)
    {
        await using var db = database.NewContext();
        return await new BookSearchQuery(db).SearchAsync(filter, paging ?? new PageRequest());
    }

    /// <summary>The filter as BookSearchService builds it (text trimmed, ISBN text without hyphens and spaces).</summary>
    private static BookSearchFilter Filter(string? text = null, Guid? categoryId = null, string? language = null, bool? available = null) =>
        new(text, text?.Replace("-", string.Empty).Replace(" ", string.Empty), categoryId, language, available);

    private static IEnumerable<Guid> Ids(PagedResult<BookSearchResultDto> result) => result.Items.Select(b => b.Id);
}
