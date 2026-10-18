using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Prepares the client's search (CAT-05) and passes it to the database query.</summary>
public sealed class BookSearchService(IBookSearchQuery query, ICatalogCoverUrlBuilder? coverUrls = null) : IBookSearchService
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

        return SearchAndBuildUrlsAsync(query, filter, request.Paging, coverUrls, cancellationToken);
    }

    private static async Task<PagedResult<BookSearchResultDto>> SearchAndBuildUrlsAsync(
        IBookSearchQuery query,
        BookSearchFilter filter,
        PageRequest paging,
        ICatalogCoverUrlBuilder? coverUrls,
        CancellationToken cancellationToken)
    {
        PagedResult<BookSearchResultDto> result = await query.SearchAsync(filter, paging, cancellationToken);
        if (coverUrls is null)
            return result;

        return new PagedResult<BookSearchResultDto>(
            result.Items.Select(book => book with { CoverImageUrl = coverUrls.Build(book.CoverImageUrl) }).ToArray(),
            result.TotalCount,
            result.Page,
            result.PageSize);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
