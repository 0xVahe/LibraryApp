using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

/// <summary>One SQL query with subqueries for the names and the copy counts; nothing is tracked.</summary>
public sealed class BookDetailsQuery(LibraryDbContext db) : IBookDetailsQuery
{
    public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        return db.Books
            .AsNoTracking()
            .Where(book => book.Id == bookId)
            .Select(book => new BookDto(
                book.Id,
                book.Title,
                book.Isbn,
                book.PublicationYear,
                book.Language,
                book.Description,
                book.CategoryId,
                db.Categories.Where(category => category.Id == book.CategoryId).Select(category => category.Name).First(),
                book.PublisherId,
                db.Publishers.Where(publisher => publisher.Id == book.PublisherId).Select(publisher => publisher.Name).FirstOrDefault(),
                db.BookAuthors
                    .Where(link => link.BookId == book.Id)
                    .Join(db.Authors, link => link.AuthorId, author => author.Id, (_, author) => author.FullName)
                    .OrderBy(name => name)
                    .ToList(),
                db.BookCopies.Count(copy => copy.BookId == book.Id
                    && copy.Status != BookCopyStatus.Lost
                    && copy.Status != BookCopyStatus.Withdrawn),
                db.BookCopies.Count(copy => copy.BookId == book.Id && copy.Status == BookCopyStatus.Available),
                book.IsDeleted,
                book.CoverImagePath))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
