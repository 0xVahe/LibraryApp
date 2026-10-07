using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class BookSearchQuery(LibraryDbContext db) : IBookSearchQuery
{
    public async Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PageRequest paging = request.Page;
        string? search = Normalize(request.Search);
        string? category = Normalize(request.Category);
        string? language = Normalize(request.Language)?.ToLowerInvariant();

        IQueryable<Book> books = db.Books
            .AsNoTracking()
            .Where(book => !book.IsDeleted);

        if (search is not null)
        {
            string isbnSearch = search.Replace("-", string.Empty, StringComparison.Ordinal);
            books = books.Where(book =>
                book.Title.Contains(search) ||
                book.Isbn.Replace("-", string.Empty).Contains(isbnSearch) ||
                db.BookAuthors.Any(link => link.BookId == book.Id &&
                    db.Authors.Any(author => author.Id == link.AuthorId && author.FullName.Contains(search))));
        }

        if (category is not null)
            books = books.Where(book => db.Categories.Any(c => c.Id == book.CategoryId && c.Name == category));

        if (language is not null)
            books = books.Where(book => book.Language == language);

        if (request.Available is not null)
        {
            IQueryable<Guid> availableBookIds = db.BookCopies
                .Where(copy => copy.Status == BookCopyStatus.Available)
                .Select(copy => copy.BookId)
                .Distinct();

            books = request.Available.Value
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
                db.BookAuthors
                    .Where(link => link.BookId == book.Id)
                    .Join(db.Authors, link => link.AuthorId, author => author.Id, (_, author) => author.FullName)
                    .OrderBy(name => name)
                    .ToList(),
                db.Categories.Where(c => c.Id == book.CategoryId).Select(c => c.Name).First(),
                book.Language,
                db.BookCopies.Count(copy => copy.BookId == book.Id && copy.Status == BookCopyStatus.Available),
                db.BookCopies.Count(copy => copy.BookId == book.Id)))
            .ToListAsync(cancellationToken);

        return new PagedResult<BookSearchResultDto>(items, totalCount, paging.Page, paging.PageSize);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
