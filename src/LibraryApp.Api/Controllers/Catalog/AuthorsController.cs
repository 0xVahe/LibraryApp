using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

/// <summary>Authors and the links between books and authors (CAT-02). Reads: any signed-in user; writes: Librarian or Administrator.</summary>
[ApiController]
[Route("api/authors")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class AuthorsController(IAuthorService authors) : ControllerBase
{
    private const string Writers = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>Lists authors (CAT-02).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AuthorDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuthorDto>>> List(CancellationToken ct) => Ok(await authors.ListAsync(ct));

    /// <summary>Gets an author (CAT-02).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AuthorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuthorDto>> Get(Guid id, CancellationToken ct) => Ok(await authors.GetAsync(id, ct));

    /// <summary>Creates an author (CAT-02).</summary>
    [HttpPost]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<AuthorDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthorDto>> Create(AuthorRequest request, CancellationToken ct)
    {
        var author = await authors.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = author.Id }, author);
    }

    /// <summary>Renames an author (CAT-02).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, AuthorRequest request, CancellationToken ct)
    {
        await authors.UpdateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Deletes an author only when no book references it, deleted books included (CAT-02, BR-CAT-5).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await authors.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Replaces a book's authors with one or more existing authors (CAT-02, BR-CAT-5).</summary>
    [HttpPut("/api/books/{bookId:guid}/authors")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetBookAuthors(Guid bookId, BookAuthorsRequest request, CancellationToken ct)
    {
        await authors.SetBookAuthorsAsync(bookId, request, ct);
        return NoContent();
    }
}
