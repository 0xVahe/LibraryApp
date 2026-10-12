using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Catalog;

public class BookTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static Book NewBook()
    {
        return new Book(Guid.NewGuid(), "Clean Code", "9780132350884", 2008, "en", null, CategoryId, null);
    }

    [Fact]
    public void NewBook_TrimsText_LowerCasesLanguage_AndStoresBlankDescriptionAsNull()
    {
        var book = new Book(Guid.NewGuid(), "  Clean Code ", " 9780132350884 ", 2008, " EN ", "   ", CategoryId, null);

        Assert.Equal("Clean Code", book.Title);
        Assert.Equal("9780132350884", book.Isbn);
        Assert.Equal("en", book.Language);
        Assert.Null(book.Description);
        Assert.False(book.IsDeleted);
        Assert.Null(book.DeletedAtUtc);
    }

    [Fact]
    public void NewBook_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new Book(Guid.Empty, "Clean Code", "9780132350884", 2008, "en", null, CategoryId, null));
    }

    [Fact]
    public void NewBook_BlankTitle_Throws()
    {
        var exception = Assert.Throws<DomainException>(
            () => new Book(Guid.NewGuid(), "  ", "9780132350884", 2008, "en", null, CategoryId, null));

        Assert.Equal(CatalogErrors.BookTitleRequired, exception.Error);
    }

    [Fact]
    public void NewBook_BlankIsbn_Throws()
    {
        var exception = Assert.Throws<DomainException>(
            () => new Book(Guid.NewGuid(), "Clean Code", "", 2008, "en", null, CategoryId, null));

        Assert.Equal(CatalogErrors.BookIsbnRequired, exception.Error);
    }

    [Fact]
    public void NewBook_BlankLanguage_Throws()
    {
        var exception = Assert.Throws<DomainException>(
            () => new Book(Guid.NewGuid(), "Clean Code", "9780132350884", 2008, " ", null, CategoryId, null));

        Assert.Equal(CatalogErrors.BookLanguageRequired, exception.Error);
    }

    [Fact]
    public void NewBook_NoCategory_Throws()
    {
        // BR-CAT-5: exactly one category.
        var exception = Assert.Throws<DomainException>(
            () => new Book(Guid.NewGuid(), "Clean Code", "9780132350884", 2008, "en", null, Guid.Empty, null));

        Assert.Equal(CatalogErrors.BookCategoryRequired, exception.Error);
    }

    [Fact]
    public void Update_ChangesTheFields()
    {
        var book = NewBook();
        var publisherId = Guid.NewGuid();
        var newCategoryId = Guid.NewGuid();

        book.Update("Clean Architecture", "9780134494166", 2017, "hy", "A book", newCategoryId, publisherId);

        Assert.Equal("Clean Architecture", book.Title);
        Assert.Equal("9780134494166", book.Isbn);
        Assert.Equal(2017, book.PublicationYear);
        Assert.Equal("hy", book.Language);
        Assert.Equal("A book", book.Description);
        Assert.Equal(newCategoryId, book.CategoryId);
        Assert.Equal(publisherId, book.PublisherId);
    }

    [Fact]
    public void Delete_MarksTheBookDeleted_AndKeepsTheData()
    {
        var book = NewBook();
        var deletedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

        book.Delete(deletedAt);

        Assert.True(book.IsDeleted);
        Assert.Equal(deletedAt, book.DeletedAtUtc);
        Assert.Equal("Clean Code", book.Title);
    }

    [Fact]
    public void Delete_AlreadyDeleted_ThrowsConflict()
    {
        var book = NewBook();
        book.Delete(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));

        var exception = Assert.Throws<DomainException>(
            () => book.Delete(new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(CatalogErrors.BookAlreadyDeleted, exception.Error);
    }
}
