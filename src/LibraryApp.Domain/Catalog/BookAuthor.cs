namespace LibraryApp.Domain.Catalog;

/// <summary>
/// Link between a book and an author. A pure link table with a composite key (BookId, AuthorId),
/// so it does not derive from Entity. A book must have at least one author (BR-CAT-5): checked by the service.
/// </summary>
public sealed class BookAuthor
{
    private BookAuthor() { }                                     // for EF Core only

    public BookAuthor(Guid bookId, Guid authorId)
    {
        BookId = bookId;
        AuthorId = authorId;
    }

    public Guid BookId { get; private set; }

    public Guid AuthorId { get; private set; }
}
