using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class BookSearchQuery(LibraryDbContext db) : IBookSearchQuery
{
    public async Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchFilter filter,
        PageRequest paging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(paging);

        IQueryable<Book> books = db.Books
            .AsNoTracking()
            .Where(book => !book.IsDeleted);

        if (filter.Text is not null)
        {
            string text = filter.Text;
            string isbnText = filter.IsbnText ?? text;

            books = books.Where(book =>
                book.Title.Contains(text) ||
                book.Isbn.Contains(isbnText) ||              
                db.BookAuthors.Any(link => link.BookId == book.Id &&
                    db.Authors.Any(author => author.Id == link.AuthorId && author.FullName.Contains(text))));
        }

        if (filter.CategoryId is Guid categoryId)
        {
            books = books.Where(book =>
                book.CategoryId == categoryId ||
                db.Categories.Any(category => category.Id == book.CategoryId && category.ParentCategoryId == categoryId));
        }

        if (filter.Language is not null)
        {
            string language = filter.Language;
            books = books.Where(book => book.Language == language);
        }

        if (filter.Available is bool available)
        {
            IQueryable<Guid> availableBookIds = db.BookCopies
                .Where(copy => copy.Status == BookCopyStatus.Available)
                .Select(copy => copy.BookId);

            books = available
                ? books.Where(book => availableBookIds.Contains(book.Id))
                : books.Where(book => !availableBookIds.Contains(book.Id));
        }

        int totalCount = await books.CountAsync(cancellationToken);

        List<BookSearchResultDto> items = await books
            .OrderBy(book => book.Title)
            .ThenBy(book => book.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(book => new BookSearchResultDto(
                book.Id,
                book.Title,
                book.Isbn,
                book.PublicationYear,
                db.BookAuthors
                    .Where(link => link.BookId == book.Id)
                    .Join(db.Authors, link => link.AuthorId, author => author.Id, (_, author) => author.FullName)
                    .OrderBy(name => name)
                    .ToList(),
                db.Categories.Where(category => category.Id == book.CategoryId).Select(category => category.Name).First(),
                book.Language,
                db.BookCopies.Count(copy => copy.BookId == book.Id && copy.Status == BookCopyStatus.Available),
                db.BookCopies.Count(copy => copy.BookId == book.Id
                    && copy.Status != BookCopyStatus.Lost
                    && copy.Status != BookCopyStatus.Withdrawn),
                book.CoverImagePath))
            .ToListAsync(cancellationToken);

        return new PagedResult<BookSearchResultDto>(items, totalCount, paging.Page, paging.PageSize);
    }
}
