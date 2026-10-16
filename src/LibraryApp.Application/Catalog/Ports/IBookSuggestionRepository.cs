using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

public interface IBookSuggestionRepository
{
    void Add(BookSuggestion suggestion);
    /// <summary>Tracked entity. Review callers must hold the suggestion's transaction lock before reading.</summary>
    Task<BookSuggestion?> FindAsync(Guid id, CancellationToken ct);
    /// <summary>Filters ownership and status in the database before counting and paging.</summary>
    Task<PagedResult<BookSuggestionDto>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
        PageRequest paging, CancellationToken ct);
}
