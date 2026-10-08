using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Catalog;

/// <summary>A real SQL Server database with three books for the book-details query (CAT-06). Written once, then only read.</summary>
public sealed class BookDetailsDatabase : IAsyncLifetime
{
    public static readonly Guid CategoryId = new("c2000000-0000-0000-0000-000000000001");
    public static readonly Guid PublisherId = new("d2000000-0000-0000-0000-000000000001");

    public static readonly Guid FullBookId = new("b2000000-0000-0000-0000-000000000001");     // 2 authors, publisher, 6 copies
    public static readonly Guid BareBookId = new("b2000000-0000-0000-0000-000000000002");     // no publisher, no copies
    public static readonly Guid DeletedBookId = new("b2000000-0000-0000-0000-000000000003");  // soft-deleted

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

        db.Categories.Add(new Category(CategoryId, "IT & Software Engineering"));
        db.Publishers.Add(new Publisher(PublisherId, "Addison-Wesley"));

        var thomas = new Author(Guid.NewGuid(), "David Thomas");
        var hunt = new Author(Guid.NewGuid(), "Andrew Hunt");
        db.Authors.AddRange(thomas, hunt);

        db.Books.Add(new Book(FullBookId, "The Pragmatic Programmer", "9781000000177", 2019, "en",
            "Practical habits for software developers.", CategoryId, PublisherId));
        db.BookAuthors.AddRange(new BookAuthor(FullBookId, thomas.Id), new BookAuthor(FullBookId, hunt.Id));

        db.Books.Add(new Book(BareBookId, "Refactoring", "9781000000191", 2018, "en", null, CategoryId, null));
        db.BookAuthors.Add(new BookAuthor(BareBookId, thomas.Id));

        var deleted = new Book(DeletedBookId, "Old Edition", "9781000000207", 2003, "en", null, CategoryId, null);
        deleted.Delete(DateTime.UtcNow);
        db.Books.Add(deleted);
        db.BookAuthors.Add(new BookAuthor(DeletedBookId, hunt.Id));

        // 2 Available + 1 OnLoan count; Lost and Withdrawn do not; UnderRepair counts as owned but not available.
        db.BookCopies.AddRange(
            Copy(FullBookId, "PP-1", BookCopyStatus.Available),
            Copy(FullBookId, "PP-2", BookCopyStatus.Available),
            Copy(FullBookId, "PP-3", BookCopyStatus.OnLoan),
            Copy(FullBookId, "PP-4", BookCopyStatus.UnderRepair),
            Copy(FullBookId, "PP-5", BookCopyStatus.Lost),
            Copy(FullBookId, "PP-6", BookCopyStatus.Withdrawn),
            Copy(DeletedBookId, "OLD-1", BookCopyStatus.Available));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static BookCopy Copy(Guid bookId, string barcode, BookCopyStatus status)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, barcode, BookCopyCondition.Good, null);
        if (status != BookCopyStatus.Available)
            copy.ChangeStatus(status);
        return copy;
    }
}

public class BookDetailsQueryTests(BookDetailsDatabase database) : IClassFixture<BookDetailsDatabase>
{
    [Fact]
    public async Task Returns_the_book_with_names_and_copy_counts()
    {
        var book = await Get(BookDetailsDatabase.FullBookId);

        Assert.NotNull(book);
        Assert.Equal("The Pragmatic Programmer", book.Title);
        Assert.Equal("9781000000177", book.Isbn);
        Assert.Equal(2019, book.PublicationYear);
        Assert.Equal("en", book.Language);
        Assert.Equal("Practical habits for software developers.", book.Description);
        Assert.Equal(BookDetailsDatabase.CategoryId, book.CategoryId);
        Assert.Equal("IT & Software Engineering", book.CategoryName);
        Assert.Equal(BookDetailsDatabase.PublisherId, book.PublisherId);
        Assert.Equal("Addison-Wesley", book.PublisherName);
        Assert.False(book.IsDeleted);
    }

    [Fact]
    public async Task Authors_are_in_alphabetical_order()
    {
        var book = await Get(BookDetailsDatabase.FullBookId);

        Assert.Equal(new[] { "Andrew Hunt", "David Thomas" }, book!.Authors);
    }

    [Fact]
    public async Task Lost_and_withdrawn_copies_are_not_counted_and_only_available_ones_are_available()
    {
        var book = await Get(BookDetailsDatabase.FullBookId);

        Assert.Equal(4, book!.TotalCopies);       // Available, Available, OnLoan, UnderRepair
        Assert.Equal(2, book.AvailableCopies);
    }

    [Fact]
    public async Task A_book_without_publisher_and_copies_has_nulls_and_zeros()
    {
        var book = await Get(BookDetailsDatabase.BareBookId);

        Assert.NotNull(book);
        Assert.Null(book.PublisherId);
        Assert.Null(book.PublisherName);
        Assert.Null(book.Description);
        Assert.Equal(0, book.TotalCopies);
        Assert.Equal(0, book.AvailableCopies);
    }

    [Fact]
    public async Task A_deleted_book_is_returned_with_IsDeleted()
    {
        var book = await Get(BookDetailsDatabase.DeletedBookId);

        Assert.NotNull(book);
        Assert.True(book.IsDeleted);
    }

    [Fact]
    public async Task An_unknown_book_is_null()
    {
        Assert.Null(await Get(Guid.NewGuid()));
    }

    private async Task<BookDto?> Get(Guid bookId)
    {
        await using var db = database.NewContext();
        return await new BookDetailsQuery(db).GetBookAsync(bookId);
    }
}
