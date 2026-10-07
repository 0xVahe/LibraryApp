using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Prepares the client's search (CAT-05) and passes it to the database query.</summary>
public sealed class BookSearchService(IBookSearchQuery query) : IBookSearchService
{
    public Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? text = Normalize(request.Search);

        var filter = new BookSearchFilter(
            Text: text,
            IsbnText: text?.Replace("-", string.Empty).Replace(" ", string.Empty),
            CategoryId: request.CategoryId,
            Language: Normalize(request.Language)?.ToLowerInvariant(),
            Available: request.Available);

        return query.SearchAsync(filter, request.Paging, cancellationToken);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
