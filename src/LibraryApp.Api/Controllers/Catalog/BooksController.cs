using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

[ApiController]
[Route("api/books")]
public sealed class BooksController(IBookSearchService searchService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<BookSearchResultDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? language,
        [FromQuery(Name = "availability")] bool? availability,
        [FromQuery(Name = "available")] bool? available,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        BookSearchRequest request = new(
            search,
            category,
            language,
            availability ?? available,
            new PageRequest(page, pageSize));
        PagedResult<BookSearchResultDto> result =
            await searchService.SearchAsync(request, cancellationToken);
        return Ok(result);
    }
}
