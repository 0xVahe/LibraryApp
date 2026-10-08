using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Book details for the catalog (CAT-06).</summary>
public interface IBookDetailsService
{
    /// <summary>The book with its copy counts. Throws <c>catalog.book_not_found</c> for an unknown or deleted book.</summary>
    Task<BookDetailsDto> GetBookDetailsAsync(Guid bookId, CancellationToken cancellationToken = default);
}
