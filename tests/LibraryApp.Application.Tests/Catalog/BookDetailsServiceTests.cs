using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Catalog;

public sealed class BookDetailsServiceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid PublisherId = Guid.NewGuid();

    [Fact]
    public async Task Returns_the_book_with_N_of_M_available()
    {
        var due = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
        var service = Service(Book(total: 3, available: 2), due);

        var details = await service.GetBookDetailsAsync(BookId);

        Assert.Equal(BookId, details.Id);
        Assert.Equal("Clean Code", details.Title);
        Assert.Equal("9781000000153", details.Isbn);
        Assert.Equal(2008, details.PublicationYear);
        Assert.Equal("en", details.Language);
        Assert.Equal("Writing readable code.", details.Description);
        Assert.Equal(CategoryId, details.CategoryId);
        Assert.Equal("IT & Software Engineering", details.CategoryName);
        Assert.Equal(PublisherId, details.PublisherId);
        Assert.Equal("Prentice Hall", details.PublisherName);
        Assert.Equal(new[] { "Robert C. Martin" }, details.Authors);
        Assert.Equal(3, details.TotalCopies);
        Assert.Equal(2, details.AvailableCopies);
        Assert.Equal("2 of 3 available", details.Availability);
        Assert.Equal(new DateOnly(2026, 10, 13), details.NextExpectedReturnDate);
    }

    [Fact]
    public async Task A_book_without_copies_shows_0_of_0_available()
    {
        var service = Service(Book(total: 0, available: 0), null);

        var details = await service.GetBookDetailsAsync(BookId);

        Assert.Equal("0 of 0 available", details.Availability);
        Assert.Null(details.NextExpectedReturnDate);
    }

    [Fact]
    public async Task An_unknown_book_is_not_found()
    {
        var service = Service(null, null);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.GetBookDetailsAsync(Guid.NewGuid()));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
    }

    [Fact]
    public async Task A_deleted_book_is_not_found()
    {
        var service = Service(Book(total: 1, available: 1) with { IsDeleted = true }, null);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.GetBookDetailsAsync(BookId));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
    }

    [Fact]
    public async Task An_empty_cover_base_url_returns_null_instead_of_the_stored_path()
    {
        var service = Service(Book(total: 1, available: 1) with { CoverImageUrl = "library-covers/01.png" }, null, "");

        var details = await service.GetBookDetailsAsync(BookId);

        Assert.Null(details.CoverImageUrl);
    }

    [Fact]
    public async Task The_stored_cover_path_comes_with_the_cover_url_so_the_Edit_form_can_keep_it()
    {
        var service = Service(Book(total: 1, available: 1) with { CoverImageUrl = "library-covers/01.png" }, null);

        var details = await service.GetBookDetailsAsync(BookId);

        Assert.Equal("library-covers/01.png", details.CoverImagePath);
        Assert.EndsWith("/library-covers/01.png", details.CoverImageUrl);
    }

    private static BookDto Book(int total, int available) => new(
        BookId, "Clean Code", "9781000000153", 2008, "en", "Writing readable code.",
        CategoryId, "IT & Software Engineering", PublisherId, "Prentice Hall",
        ["Robert C. Martin"], total, available, IsDeleted: false);

    private static BookDetailsService Service(BookDto? book, DateTime? dueDateUtc,
        string baseUrl = "https://images.example.test/") =>
        new(new StubQuery(book), new FakeLoanQuery(dueDateUtc), new YerevanClock(),
            new CatalogCoverUrlBuilder(Options.Create(new CatalogCoversOptions
            {
                BaseUrl = baseUrl,
            })));

    /// <summary>Returns the same book (or null) for any id.</summary>
    private sealed class StubQuery(BookDto? book) : IBookDetailsQuery
    {
        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken cancellationToken = default) =>
            Task.FromResult(book);
    }

    private sealed class FakeLoanQuery(DateTime? dueDateUtc) : ILoanQueryService
    {
        public Task<DateTime?> GetNextDueDateAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult(dueDateUtc);
    }

    private sealed class YerevanClock : IClock
    {
        public DateTime UtcNow => throw new NotSupportedException();
        public DateOnly LocalToday => throw new NotSupportedException();
        public DateTime ToLocal(DateTime utc) => utc.AddHours(4);
        public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));
        public DateTime StartOfLocalDayUtc(DateOnly localDate) => throw new NotSupportedException();
        public DateTime EndOfLocalDayUtc(DateOnly localDate) => throw new NotSupportedException();
    }
}
