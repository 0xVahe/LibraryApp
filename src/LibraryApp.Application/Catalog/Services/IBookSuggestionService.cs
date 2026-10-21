using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Book purchase suggestions (CAT-10): staff suggest, the Library Administrator approves or rejects.</summary>
public interface IBookSuggestionService
{
    Task<BookSuggestionDto> CreateAsync(CreateBookSuggestionRequest request, CancellationToken ct = default);
    Task<PagedResult<BookSuggestionDto>> ListMineAsync(PageRequest paging, CancellationToken ct = default);
    Task<PagedResult<BookSuggestionDto>> ListAsync(BookSuggestionListRequest request, CancellationToken ct = default);
    Task<BookSuggestionDto> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<BookSuggestionDto> RejectAsync(Guid id, RejectBookSuggestionRequest request, CancellationToken ct = default);
}
