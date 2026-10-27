using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Validators;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Prepares the client's search (CAT-05) and passes it to the database query.</summary>
public sealed class BookSearchService(IBookSearchQuery query, ICatalogCoverUrlBuilder coverUrls) : IBookSearchService
{
    public Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? text = Normalize(request.Search);

        var filter = new BookSearchFilter(
            Text: text,
            IsbnText: IsbnText(text),
            CategoryId: request.CategoryId,
            Language: Normalize(request.Language)?.ToLowerInvariant(),
            Available: request.Available);

        return SearchAndBuildUrlsAsync(query, filter, request.Paging, coverUrls, cancellationToken);
    }

    private static async Task<PagedResult<BookSearchResultDto>> SearchAndBuildUrlsAsync(
        IBookSearchQuery query,
        BookSearchFilter filter,
        PageRequest paging,
        ICatalogCoverUrlBuilder coverUrls,
        CancellationToken cancellationToken)
    {
        PagedResult<BookSearchResultDto> result = await query.SearchAsync(filter, paging, cancellationToken);
        return new PagedResult<BookSearchResultDto>(
            result.Items.Select(book => book with { CoverImageUrl = coverUrls.Build(book.CoverImageUrl) }).ToArray(),
            result.TotalCount,
            result.Page,
            result.PageSize);
    }

    /// <summary>
    /// The search text as it is compared with the stored ISBNs, which are 13 digits (BR-CAT-1): a whole ISBN-10 or
    /// ISBN-13 becomes that ISBN-13, so an ISBN-10 finds its book too; any other text loses its hyphens and spaces,
    /// so part of an ISBN still matches.
    /// </summary>
    private static string? IsbnText(string? text) =>
        text is null ? null
        : IsbnRules.TryNormalize(text, out var isbn) ? isbn
        : text.Replace("-", string.Empty).Replace(" ", string.Empty);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
