using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Ports;

public interface IBookSearchService
{
    Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchRequest request,
        CancellationToken cancellationToken = default);
}
