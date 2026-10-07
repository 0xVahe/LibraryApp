using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

[ApiController]
[Route("api/books")]
public sealed class BooksController(IBookSearchService searchService) : ControllerBase
{
    /// <summary>Searches the catalog (CAT-05). Deleted books are never returned; results are ordered by title.</summary>
    /// <param name="search">Part of the title, an author name or an ISBN (hyphens allowed).</param>
    /// <param name="categoryId">A category; a top-level category also returns the books of its subcategories.</param>
    /// <param name="language">ISO 639-1 code, for example "en".</param>
    /// <param name="available">true: books with at least one Available copy; false: books without one.</param>
    /// <param name="page">Page number, from 1.</param>
    /// <param name="pageSize">Items per page, default 20, at most 100.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<BookSearchResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BookSearchResultDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? language,
        [FromQuery] bool? available,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new BookSearchRequest(search, categoryId, language, available, new PageRequest(page, pageSize));
        PagedResult<BookSearchResultDto> result = await searchService.SearchAsync(request, cancellationToken);
        return Ok(result);
    }
}
