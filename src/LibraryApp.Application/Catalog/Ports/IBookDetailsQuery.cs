using LibraryApp.Application.Abstractions.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>
/// Reads one book with its category, publisher and author names and its copy counts (CAT-06). Implemented in
/// Infrastructure. A soft-deleted book is returned too, with IsDeleted = true: the details page hides it, while
/// <see cref="ICatalogService.GetBookAsync"/> must still find it for old loans.
/// <c>CoverImageUrl</c> holds the stored cover path here (for example "library-covers/01.png"): the services turn it
/// into the public URL with <see cref="Services.ICatalogCoverUrlBuilder"/>.
/// </summary>
public interface IBookDetailsQuery
{
    /// <summary>The book, or null when the id is unknown.</summary>
    Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken cancellationToken = default);
}
