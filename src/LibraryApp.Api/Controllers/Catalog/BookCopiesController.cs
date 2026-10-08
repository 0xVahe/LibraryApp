using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

/// <summary>
/// Physical copies of a book (CAT-04). Reads: any signed-in user; writes: Librarian or Administrator (NFR-03).
/// </summary>
[ApiController]
[Route("api/books/{bookId:guid}/copies")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class BookCopiesController(IBookCopyService copies) : ControllerBase
{
    private const string Writers = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>All copies of the book, retired ones included, ordered by barcode.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BookCopyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BookCopyDto>>> GetAll(Guid bookId, CancellationToken ct)
    {
        return Ok(await copies.GetCopiesAsync(bookId, ct));
    }

    /// <summary>One copy of the book.</summary>
    [HttpGet("{copyId:guid}")]
    [ProducesResponseType<BookCopyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookCopyDto>> GetById(Guid bookId, Guid copyId, CancellationToken ct)
    {
        return Ok(await copies.GetCopyAsync(bookId, copyId, ct));
    }

    /// <summary>Registers a new copy; it starts as Available. The barcode must be unique across all copies (BR-CAT-2).</summary>
    [HttpPost]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<BookCopyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookCopyDto>> Add(Guid bookId, AddCopyRequest request, CancellationToken ct)
    {
        BookCopyDto copy = await copies.AddCopyAsync(bookId, request, ct);
        return CreatedAtAction(nameof(GetById), new { bookId, copyId = copy.Id }, copy);
    }

    /// <summary>Changes the condition and the shelf location of a copy.</summary>
    [HttpPut("{copyId:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<BookCopyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookCopyDto>> Update(Guid bookId, Guid copyId, UpdateCopyRequest request, CancellationToken ct)
    {
        return Ok(await copies.UpdateCopyAsync(bookId, copyId, request, ct));
    }

    /// <summary>
    /// Takes a copy out of circulation (UnderRepair, Lost, Withdrawn) or brings it back from repair (Available), BR-CAT-4.
    /// A copy that is OnLoan or Reserved is changed only through Borrowing.
    /// </summary>
    [HttpPost("{copyId:guid}/status")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<BookCopyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BookCopyDto>> ChangeStatus(Guid bookId, Guid copyId, ChangeCopyStatusRequest request, CancellationToken ct)
    {
        return Ok(await copies.ChangeStatusAsync(bookId, copyId, request, ct));
    }
}
