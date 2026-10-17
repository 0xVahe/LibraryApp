using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

public interface IBookSuggestionService
{
    Task<BookSuggestionDto> CreateAsync(CreateBookSuggestionRequest request, CancellationToken ct = default);
    Task<PagedResult<BookSuggestionDto>> ListMineAsync(PageRequest paging, CancellationToken ct = default);
    Task<PagedResult<BookSuggestionDto>> ListAsync(BookSuggestionListRequest request, CancellationToken ct = default);
    Task<BookSuggestionDto> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<BookSuggestionDto> RejectAsync(Guid id, RejectBookSuggestionRequest request, CancellationToken ct = default);
}
