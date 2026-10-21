using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>Book suggestions (CAT-10).</summary>
public interface IBookSuggestionRepository
{
    void Add(BookSuggestion suggestion);
    /// <summary>Tracked entity. Review callers must hold the suggestion's transaction lock before reading.</summary>
    Task<BookSuggestion?> FindAsync(Guid id, CancellationToken ct);
    /// <summary>
    /// Filters ownership and status in the database before counting and paging; newest first. Not tracked.
    /// Staff names are not part of this port: the service reads them through the Staff contract.
    /// </summary>
    Task<PagedResult<BookSuggestion>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
        PageRequest paging, CancellationToken ct);
}
