using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Book search for all staff (CAT-05).</summary>
public interface IBookSearchService
{
    Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchRequest request,
        CancellationToken cancellationToken = default);
}
