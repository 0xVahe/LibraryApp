using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Shows one book with "N of M available" (CAT-06). A deleted book is hidden, like in the search.</summary>
public sealed class BookDetailsService(IBookDetailsQuery query) : IBookDetailsService
{
    public async Task<BookDetailsDto> GetBookDetailsAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        BookDto? book = await query.GetBookAsync(bookId, cancellationToken);

        if (book is null || book.IsDeleted)
            throw new DomainException(CatalogErrors.BookNotFound);

        return new BookDetailsDto(
            book.Id,
            book.Title,
            book.Isbn,
            book.PublicationYear,
            book.Language,
            book.Description,
            book.CategoryId,
            book.CategoryName,
            book.PublisherId,
            book.PublisherName,
            book.Authors,
            book.TotalCopies,
            book.AvailableCopies,
            Availability: $"{book.AvailableCopies} of {book.TotalCopies} available");
    }
}
