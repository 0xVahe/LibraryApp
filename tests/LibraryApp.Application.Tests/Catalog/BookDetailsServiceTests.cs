using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public sealed class BookDetailsServiceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid PublisherId = Guid.NewGuid();

    [Fact]
    public async Task Returns_the_book_with_N_of_M_available()
    {
        var service = new BookDetailsService(new StubQuery(Book(total: 3, available: 2)));

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
    }

    [Fact]
    public async Task A_book_without_copies_shows_0_of_0_available()
    {
        var service = new BookDetailsService(new StubQuery(Book(total: 0, available: 0)));

        var details = await service.GetBookDetailsAsync(BookId);

        Assert.Equal("0 of 0 available", details.Availability);
    }

    [Fact]
    public async Task An_unknown_book_is_not_found()
    {
        var service = new BookDetailsService(new StubQuery(null));

        var error = await Assert.ThrowsAsync<DomainException>(() => service.GetBookDetailsAsync(Guid.NewGuid()));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
    }

    [Fact]
    public async Task A_deleted_book_is_not_found()
    {
        var service = new BookDetailsService(new StubQuery(Book(total: 1, available: 1) with { IsDeleted = true }));

        var error = await Assert.ThrowsAsync<DomainException>(() => service.GetBookDetailsAsync(BookId));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
    }

    private static BookDto Book(int total, int available) => new(
        BookId, "Clean Code", "9781000000153", 2008, "en", "Writing readable code.",
        CategoryId, "IT & Software Engineering", PublisherId, "Prentice Hall",
        ["Robert C. Martin"], total, available, IsDeleted: false);

    /// <summary>Returns the same book (or null) for any id.</summary>
    private sealed class StubQuery(BookDto? book) : IBookDetailsQuery
    {
        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken cancellationToken = default) =>
            Task.FromResult(book);
    }
}
