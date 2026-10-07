using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

public sealed class BookSearchService(IBookSearchQuery query) : IBookSearchService
{
    public Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchRequest request,
        CancellationToken cancellationToken = default) =>
        query.SearchAsync(request, cancellationToken);
}
