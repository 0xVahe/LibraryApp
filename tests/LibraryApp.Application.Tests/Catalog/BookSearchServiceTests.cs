using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Tests.Catalog;

public sealed class BookSearchServiceTests
{
    [Fact]
    public async Task Search_delegates_the_request_and_cancellation_to_the_query()
    {
        var query = new RecordingQuery();
        var service = new BookSearchService(query);
        using var cancellation = new CancellationTokenSource();
        var request = new BookSearchRequest(
            "Risk",
            "Risk, Compliance & AML",
            "en",
            true,
            new PageRequest(2, 20));

        PagedResult<BookSearchResultDto> result = await service.SearchAsync(request, cancellation.Token);

        Assert.Same(request, query.Request);
        Assert.Equal(cancellation.Token, query.CancellationToken);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private sealed class RecordingQuery : IBookSearchQuery
    {
        public BookSearchRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PagedResult<BookSearchResultDto>> SearchAsync(
            BookSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(new PagedResult<BookSearchResultDto>(
                [], 0, request.Page.Page, request.Page.PageSize));
        }
    }
}
