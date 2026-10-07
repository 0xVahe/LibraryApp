using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Tests.Catalog;

public sealed class BookSearchServiceTests
{
    private readonly RecordingQuery _query = new();
    private readonly BookSearchService _service;

    public BookSearchServiceTests()
    {
        _service = new BookSearchService(_query);
    }

    [Fact]
    public async Task The_search_text_is_trimmed_and_the_isbn_text_has_no_hyphens_or_spaces()
    {
        await _service.SearchAsync(Request(search: "  978-0-13 475759-9 "));

        Assert.Equal("978-0-13 475759-9", _query.Filter!.Text);
        Assert.Equal("9780134757599", _query.Filter.IsbnText);
    }

    [Fact]
    public async Task The_language_is_trimmed_and_lower_case()
    {
        await _service.SearchAsync(Request(language: " EN "));

        Assert.Equal("en", _query.Filter!.Language);
    }

    [Fact]
    public async Task Empty_values_mean_no_filter()
    {
        await _service.SearchAsync(Request(search: "   ", language: ""));

        Assert.Null(_query.Filter!.Text);
        Assert.Null(_query.Filter.IsbnText);
        Assert.Null(_query.Filter.Language);
    }

    [Fact]
    public async Task Category_availability_paging_and_cancellation_are_passed_unchanged()
    {
        var categoryId = Guid.NewGuid();
        var paging = new PageRequest(2, 50);
        using var cancellation = new CancellationTokenSource();

        await _service.SearchAsync(new BookSearchRequest(null, categoryId, null, false, paging), cancellation.Token);

        Assert.Equal(categoryId, _query.Filter!.CategoryId);
        Assert.False(_query.Filter.Available);
        Assert.Same(paging, _query.Paging);
        Assert.Equal(cancellation.Token, _query.CancellationToken);
    }

    [Fact]
    public async Task A_missing_request_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SearchAsync(null!));
    }

    private static BookSearchRequest Request(string? search = null, string? language = null) =>
        new(search, null, language, null, new PageRequest());

    private sealed class RecordingQuery : IBookSearchQuery
    {
        public BookSearchFilter? Filter { get; private set; }
        public PageRequest? Paging { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PagedResult<BookSearchResultDto>> SearchAsync(
            BookSearchFilter filter,
            PageRequest paging,
            CancellationToken cancellationToken = default)
        {
            Filter = filter;
            Paging = paging;
            CancellationToken = cancellationToken;
            return Task.FromResult(new PagedResult<BookSearchResultDto>([], 0, paging.Page, paging.PageSize));
        }
    }
}
