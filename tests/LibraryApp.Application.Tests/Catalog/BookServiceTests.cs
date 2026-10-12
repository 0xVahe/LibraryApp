using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Tests.Catalog;

public class BookServiceTests
{
    private readonly FakeBooks _books = new();
    private readonly FakeAuthors _authors = new();
    private readonly TestTransaction _transaction = new();
    private readonly FakeClock _clock = new();

    private BookService Service => new(
        _books,
        _authors,
        _transaction,
        _transaction,
        _transaction,
        _clock);

    [Fact]
    public async Task Create_adds_book_and_author_links()
    {
        var categoryId = Guid.NewGuid();
        var author = new Author(Guid.NewGuid(), "Demo Author");

        _books.CategoryIds.Add(categoryId);
        _authors.Authors.Add(author);

        var request = new CreateBookRequest(
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            "A book about software craftsmanship.",
            categoryId,
            null,
            [author.Id]);

        var id = await Service.CreateAsync(request);

        var book = Assert.Single(_books.Books);
        Assert.Equal(id, book.Id);
        Assert.Equal("Clean Code", book.Title);
        Assert.Equal("9780132350884", book.Isbn);

        var link = Assert.Single(_authors.Links);
        Assert.Equal(id, link.BookId);
        Assert.Equal(author.Id, link.AuthorId);

        Assert.Contains("catalog:books", _transaction.Locks);
        Assert.False(_transaction.Active);
    }

    [Fact]
    public async Task Create_fails_when_category_does_not_exist()
    {
        var author = new Author(Guid.NewGuid(), "Demo Author");
        _authors.Authors.Add(author);

        var request = new CreateBookRequest(
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            Guid.NewGuid(),
            null,
            [author.Id]);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.CreateAsync(request));

        Assert.Equal(CatalogErrors.CategoryNotFound, error.Error);
        Assert.Empty(_books.Books);
        Assert.Empty(_authors.Links);
    }

    [Fact]
    public async Task Create_fails_when_isbn_already_exists()
    {
        var categoryId = Guid.NewGuid();
        var author = new Author(Guid.NewGuid(), "Demo Author");

        _books.CategoryIds.Add(categoryId);
        _authors.Authors.Add(author);
        _books.Books.Add(new Book(
            Guid.NewGuid(),
            "Existing Book",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null));

        var request = new CreateBookRequest(
            "Another Book",
            "9780132350884",
            2020,
            "en",
            null,
            categoryId,
            null,
            [author.Id]);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.CreateAsync(request));

        Assert.Equal(CatalogErrors.BookIsbnDuplicate, error.Error);
        Assert.Single(_books.Books);
        Assert.Empty(_authors.Links);
    }

    [Fact]
    public async Task Create_fails_when_author_does_not_exist()
    {
        var categoryId = Guid.NewGuid();

        _books.CategoryIds.Add(categoryId);

        var request = new CreateBookRequest(
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null,
            [Guid.NewGuid()]);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.CreateAsync(request));

        Assert.Equal(CatalogErrors.AuthorNotFound, error.Error);
        Assert.Empty(_books.Books);
        Assert.Empty(_authors.Links);
    }

    [Fact]
    public async Task Create_fails_when_publisher_does_not_exist()
    {
        var categoryId = Guid.NewGuid();
        var author = new Author(Guid.NewGuid(), "Demo Author");

        _books.CategoryIds.Add(categoryId);
        _authors.Authors.Add(author);

        var request = new CreateBookRequest(
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            Guid.NewGuid(),
            [author.Id]);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.CreateAsync(request));

        Assert.Equal(CatalogErrors.PublisherNotFound, error.Error);
        Assert.Empty(_books.Books);
        Assert.Empty(_authors.Links);
    }

    [Fact]
    public async Task Create_fails_when_author_list_is_empty()
    {
        var request = new CreateBookRequest(
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            Guid.NewGuid(),
            null,
            []);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.CreateAsync(request));

        Assert.Equal(CatalogErrors.BookAuthorsRequired, error.Error);
        Assert.Empty(_books.Books);
        Assert.Empty(_authors.Links);
    }

    [Fact]
    public async Task Update_changes_book_details()
    {
        var categoryId = Guid.NewGuid();
        var book = new Book(
            Guid.NewGuid(),
            "Old Title",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null);

        _books.CategoryIds.Add(categoryId);
        _books.Books.Add(book);

        var request = new UpdateBookRequest(
            "New Title",
            "9780132350884",
            2020,
            "fr",
            "Updated description.",
            categoryId,
            null);

        await Service.UpdateAsync(book.Id, request);

        Assert.Equal("New Title", book.Title);
        Assert.Equal(2020, book.PublicationYear);
        Assert.Equal("fr", book.Language);
        Assert.Equal("Updated description.", book.Description);
        Assert.Contains("catalog:books", _transaction.Locks);
        Assert.False(_transaction.Active);
    }

    [Fact]
    public async Task Update_fails_when_book_does_not_exist()
    {
        var request = new UpdateBookRequest(
            "New Title",
            "9780132350884",
            2020,
            "en",
            null,
            Guid.NewGuid(),
            null);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.UpdateAsync(Guid.NewGuid(), request));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
        Assert.Empty(_books.Books);
    }

    [Fact]
    public async Task Update_fails_when_isbn_belongs_to_another_book()
    {
        var categoryId = Guid.NewGuid();

        var book = new Book(
            Guid.NewGuid(),
            "First Book",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null);

        var anotherBook = new Book(
            Guid.NewGuid(),
            "Second Book",
            "9781492056812",
            2020,
            "en",
            null,
            categoryId,
            null);

        _books.CategoryIds.Add(categoryId);
        _books.Books.AddRange([book, anotherBook]);

        var request = new UpdateBookRequest(
            "First Book Updated",
            anotherBook.Isbn,
            2021,
            "en",
            null,
            categoryId,
            null);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.UpdateAsync(book.Id, request));

        Assert.Equal(CatalogErrors.BookIsbnDuplicate, error.Error);
        Assert.Equal("First Book", book.Title);
        Assert.Equal("9780132350884", book.Isbn);
    }

    [Fact]
    public async Task Update_allows_book_to_keep_its_own_isbn()
    {
        var categoryId = Guid.NewGuid();
        var book = new Book(
            Guid.NewGuid(),
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null);

        _books.CategoryIds.Add(categoryId);
        _books.Books.Add(book);

        var request = new UpdateBookRequest(
            "Clean Code, Updated",
            book.Isbn,
            2020,
            "en",
            null,
            categoryId,
            null);

        await Service.UpdateAsync(book.Id, request);

        Assert.Equal("Clean Code, Updated", book.Title);
        Assert.Equal("9780132350884", book.Isbn);
    }

    [Fact]
    public async Task Update_fails_when_book_is_deleted()
    {
        var categoryId = Guid.NewGuid();
        var book = new Book(
            Guid.NewGuid(),
            "Deleted Book",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null);

        book.Delete(DateTime.UtcNow);

        _books.CategoryIds.Add(categoryId);
        _books.Books.Add(book);

        var request = new UpdateBookRequest(
            "Updated Title",
            "9780132350884",
            2020,
            "en",
            null,
            categoryId,
            null);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.UpdateAsync(book.Id, request));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
        Assert.Equal("Deleted Book", book.Title);
    }

    [Fact]
    public async Task Delete_soft_deletes_book()
    {
        var categoryId = Guid.NewGuid();
        var book = new Book(
            Guid.NewGuid(),
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null);

        _books.Books.Add(book);
        _clock.UtcNow = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

        await Service.DeleteAsync(book.Id);

        Assert.True(book.IsDeleted);
        Assert.Equal(_clock.UtcNow, book.DeletedAtUtc);
        Assert.Contains(book, _books.Books);
    }

    [Fact]
    public async Task Delete_withdraws_available_and_under_repair_copies_but_preserves_lost_and_other_books()
    {
        var book = new Book(
            Guid.NewGuid(),
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            Guid.NewGuid(),
            null);

        var otherBook = new Book(
            Guid.NewGuid(),
            "Refactoring",
            "9780201485677",
            1999,
            "en",
            null,
            Guid.NewGuid(),
            null);

        _books.Books.Add(book);
        _books.Books.Add(otherBook);

        var availableCopy = new BookCopy(
            Guid.NewGuid(),
            book.Id,
            "BC-DELETE-AVAILABLE",
            BookCopyCondition.Good,
            "A-01");

        var repairCopy = new BookCopy(
            Guid.NewGuid(),
            book.Id,
            "BC-DELETE-REPAIR",
            BookCopyCondition.Good,
            "A-02");

        repairCopy.ChangeStatus(BookCopyStatus.UnderRepair);

        var lostCopy = new BookCopy(
            Guid.NewGuid(),
            book.Id,
            "BC-DELETE-LOST",
            BookCopyCondition.Good,
            "A-03");

        lostCopy.ChangeStatus(BookCopyStatus.Lost);

        var otherBookCopy = new BookCopy(
            Guid.NewGuid(),
            otherBook.Id,
            "BC-OTHER-BOOK",
            BookCopyCondition.Good,
            "B-01");

        _books.Copies.AddRange(
            [availableCopy, repairCopy, lostCopy, otherBookCopy]);

        await Service.DeleteAsync(book.Id);

        Assert.True(book.IsDeleted);
        Assert.Equal(BookCopyStatus.Withdrawn, availableCopy.Status);
        Assert.Equal(BookCopyStatus.Withdrawn, repairCopy.Status);
        Assert.Equal(BookCopyStatus.Lost, lostCopy.Status);
        Assert.Equal(BookCopyStatus.Available, otherBookCopy.Status);
    }

    [Fact]
    public async Task Delete_fails_when_book_does_not_exist()
    {
        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.DeleteAsync(Guid.NewGuid()));

        Assert.Equal(CatalogErrors.BookNotFound, error.Error);
        Assert.Empty(_books.Books);
    }

    [Fact]
    public async Task Delete_fails_when_book_has_copies_in_circulation()
    {
        var book = new Book(
            Guid.NewGuid(),
            "Clean Code",
            "9780132350884",
            2008,
            "en",
            null,
            Guid.NewGuid(),
            null);

        _books.Books.Add(book);

        var copy = new BookCopy(
            Guid.NewGuid(),
            book.Id,
            "BC-DELETE-01",
            BookCopyCondition.Good,
            "A-01");

        copy.ChangeStatus(BookCopyStatus.OnLoan);
        _books.Copies.Add(copy);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.DeleteAsync(book.Id));

        Assert.Equal(CatalogErrors.BookHasCopiesInCirculation, error.Error);
        Assert.False(book.IsDeleted);
        Assert.Null(book.DeletedAtUtc);
    }


    [Fact]
    public async Task Create_normalizes_hyphenated_isbn10_to_isbn13()
    {
        var categoryId = Guid.NewGuid();
        var author = new Author(Guid.NewGuid(), "Demo Author");

        _books.CategoryIds.Add(categoryId);
        _authors.Authors.Add(author);

        var request = new CreateBookRequest(
            "Clean Code",
            "0-13-235088-2",
            2008,
            "en",
            null,
            categoryId,
            null,
            [author.Id]);

        await Service.CreateAsync(request);

        var book = Assert.Single(_books.Books);
        Assert.Equal("9780132350884", book.Isbn);
    }

    [Fact]
    public async Task Create_rejects_duplicate_isbn_with_different_format()
    {
        var categoryId = Guid.NewGuid();
        var author = new Author(Guid.NewGuid(), "Demo Author");

        _books.CategoryIds.Add(categoryId);
        _authors.Authors.Add(author);

        _books.Books.Add(new Book(
            Guid.NewGuid(),
            "Existing Book",
            "9780132350884",
            2008,
            "en",
            null,
            categoryId,
            null));

        var request = new CreateBookRequest(
            "Another Book",
            "0-13-235088-2",
            2020,
            "en",
            null,
            categoryId,
            null,
            [author.Id]);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => Service.CreateAsync(request));

        Assert.Equal(CatalogErrors.BookIsbnDuplicate, error.Error);
        Assert.Single(_books.Books);
        Assert.Empty(_authors.Links);
    }
}