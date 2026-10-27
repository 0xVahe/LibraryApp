using LibraryApp.Api.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using Microsoft.AspNetCore.Mvc;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;

namespace LibraryApp.Api.Controllers.Catalog;

/// <summary>
/// Books of the catalog (CAT-01, CAT-05, CAT-06, CAT-09). Reads: any signed-in user; writes: Librarian or Administrator.
/// </summary>
[ApiController]
[Route("api/books")]
[Authorize]
public sealed class BooksController(
    IBookSearchService searchService,
    IBookDetailsService detailsService,
    IBookService bookService,
    IBookImportService importService) : ControllerBase
{
    private const string Writers =
        StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
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

    /// <summary>
    /// Book details with total and available copies, for example "2 of 3 available" (CAT-06).
    /// Lost and Withdrawn copies are not counted. A deleted book answers 404.
    /// </summary>
    /// <param name="id">Book id.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookDetailsDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        BookDetailsDto book = await detailsService.GetBookDetailsAsync(id, cancellationToken);
        return Ok(book);
    }

    /// <summary>Creates a book (CAT-01) and returns it like GET /api/books/{id}.</summary>
    [HttpPost]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<BookDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookDetailsDto>> Create(
        CreateBookRequest request,
        CancellationToken cancellationToken)
    {
        var id = await bookService.CreateAsync(request, cancellationToken);
        BookDetailsDto book = await detailsService.GetBookDetailsAsync(id, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, book);
    }

    /// <summary>Updates a book (CAT-01). Every field is replaced; the authors are changed with PUT /api/books/{id}/authors.</summary>
    /// <remarks>
    /// Send the current <c>coverImagePath</c> (GET /api/books/{id} returns it) to keep the cover: an empty or missing value
    /// removes it. The ISBN must stay unique among active books (BR-CAT-1); a deleted book answers 404.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateBookRequest request,
        CancellationToken cancellationToken)
    {
        await bookService.UpdateAsync(id, request, cancellationToken);

        return NoContent();
    }

    /// <summary>Imports books from a CSV file (CAT-09). All rows are saved together, or none are.</summary>
    /// <remarks>
    /// Upload multipart/form-data with the <c>file</c> field: UTF-8, comma separated, a header line first, within the
    /// <c>Catalog:Import</c> settings (500 book rows and 1 MB by default).
    /// Columns: Title, Isbn, PublicationYear, Language (hy, en or ru), Category, Authors (names separated by ';'),
    /// and the optional Description, Publisher and CoverImagePath (a path in the cover storage, checked like on the book
    /// form). Category, publisher and author names must already exist.
    /// A 400 <c>catalog.import_invalid</c> answer lists every problem in <c>rows</c> (rowNumber, field, message).
    /// <code>
    /// Title,Isbn,PublicationYear,Language,Category,Authors,Publisher,Description,CoverImagePath
    /// Example Imported Book,9780132350884,2020,en,IT &amp; Software Engineering,Robert C. Martin,Prentice Hall,Imported from CSV,
    /// </code>
    /// Sample file with more cases (two authors, quoted commas, Russian and Armenian titles, no publisher): /samples/book-import.csv.
    /// </remarks>
    /// <param name="file">The CSV file.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    [HttpPost("import")]
    [Authorize(Roles = Writers)]
    [Consumes("multipart/form-data")]
    [CatalogImportRequestLimits]
    [ProducesResponseType<BookImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BookImportResult>> Import(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await importService.ImportAsync(stream, file.Length, cancellationToken));
    }

    /// <summary>Soft-deletes a book (CAT-01, BR-CAT-3).</summary>
    /// <remarks>
    /// The book leaves the catalog and its copies are withdrawn; Lost copies stay Lost. While a copy is on loan or reserved,
    /// the answer is 409 <c>catalog.book_has_copies_in_circulation</c> and nothing changes.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await bookService.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
