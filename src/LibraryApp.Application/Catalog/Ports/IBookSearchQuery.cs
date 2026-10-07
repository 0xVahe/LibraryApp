using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Ports;

/// <summary>The search in the database (CAT-05). Implemented in Infrastructure; deleted books are never returned.</summary>
public interface IBookSearchQuery
{
    Task<PagedResult<BookSearchResultDto>> SearchAsync(
        BookSearchFilter filter,
        PageRequest paging,
        CancellationToken cancellationToken = default);
}
