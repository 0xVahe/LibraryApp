using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Catalog;

/// <summary>Author and Publisher: a name is required and is trimmed.</summary>
public class NameEntitiesTests
{
    [Fact]
    public void Author_NameIsTrimmed()
    {
        var author = new Author(Guid.NewGuid(), "  Robert C. Martin ");

        Assert.Equal("Robert C. Martin", author.FullName);
    }

    [Fact]
    public void Author_BlankName_Throws()
    {
        var exception = Assert.Throws<DomainException>(() => new Author(Guid.NewGuid(), ""));

        Assert.Equal(CatalogErrors.AuthorNameRequired, exception.Error);
    }

    [Fact]
    public void Publisher_NameIsTrimmed()
    {
        var publisher = new Publisher(Guid.NewGuid(), " Prentice Hall ");

        Assert.Equal("Prentice Hall", publisher.Name);
    }

    [Fact]
    public void Publisher_Rename_BlankName_ThrowsAndKeepsTheOldName()
    {
        var publisher = new Publisher(Guid.NewGuid(), "Prentice Hall");

        var exception = Assert.Throws<DomainException>(() => publisher.Rename("  "));

        Assert.Equal(CatalogErrors.PublisherNameRequired, exception.Error);
        Assert.Equal("Prentice Hall", publisher.Name);
    }

    [Fact]
    public void BookAuthor_StoresBothIds()
    {
        var bookId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var link = new BookAuthor(bookId, authorId);

        Assert.Equal(bookId, link.BookId);
        Assert.Equal(authorId, link.AuthorId);
    }
}
