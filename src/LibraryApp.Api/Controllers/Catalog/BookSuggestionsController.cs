using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

/// <summary>Book purchase suggestions (CAT-10).</summary>
[ApiController]
[Route("api/book-suggestions")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class BookSuggestionsController(IBookSuggestionService suggestions) : ControllerBase
{
    /// <summary>Staff Member: creates a Pending purchase suggestion owned by the authenticated staff member.</summary>
    /// <remarks>
    /// Title (required, 300 characters), AuthorName (required, 200), Reason (required, 1000),
    /// and optional Isbn (valid ISBN-10 or ISBN-13; spaces and hyphens accepted, stored as ISBN-13).
    /// Ownership, status, timestamps and reviewer
    /// fields are server supplied. A StaffMember claim is required; an account without one receives 403.
    /// Returns the created suggestion, including its ID, status and UTC creation time.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = StaffRoles.StaffMember)]
    [ProducesResponseType<BookSuggestionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookSuggestionDto>> Create(CreateBookSuggestionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await suggestions.CreateAsync(request, ct));

    /// <summary>Staff Member: lists only the authenticated staff member's suggestions, newest first.</summary>
    /// <remarks>
    /// A StaffMember claim is required (otherwise 403). No ownership query parameter is accepted.
    /// Returns Items, TotalCount, Page, PageSize and TotalPages. Ties are ordered by ID.
    /// </remarks>
    /// <param name="page">Page from 1; smaller values normalize to 1.</param>
    /// <param name="pageSize">Default 20; nonpositive values normalize to 20; values over 100 normalize to 100.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("me")]
    [Authorize(Roles = StaffRoles.StaffMember)]
    [ProducesResponseType<PagedResult<BookSuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BookSuggestionDto>>> Mine([FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize, CancellationToken ct = default) =>
        Ok(await suggestions.ListMineAsync(new PageRequest(page, pageSize), ct));

    /// <summary>Administrator: lists all suggestions with optional status filtering and pagination.</summary>
    /// <remarks>
    /// Status may be Pending (1), Approved (2), or Rejected (3); omit it for all statuses. Unknown values return 400.
    /// Page defaults to 1 (minimum 1); PageSize defaults to 20 (maximum 100). Nonpositive size normalizes to 20.
    /// Results are newest first, then ID, with Items, TotalCount, Page, PageSize and TotalPages.
    /// Staff Members and other non-administrators receive 403.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<PagedResult<BookSuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<BookSuggestionDto>>> List([FromQuery] BookSuggestionListRequest request,
        CancellationToken ct) => Ok(await suggestions.ListAsync(request, ct));

    /// <summary>Administrator: approves a Pending suggestion without creating a Book or BookCopy.</summary>
    /// <remarks>
    /// No request body. Returns the reviewed suggestion with the administrator's user ID and UTC review time.
    /// An unknown ID returns 404; any already reviewed suggestion returns 409. A librarian adds the book separately.
    /// </remarks>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<BookSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookSuggestionDto>> Approve(Guid id, CancellationToken ct) =>
        Ok(await suggestions.ApproveAsync(id, ct));

    /// <summary>Administrator: rejects a Pending suggestion with a required comment.</summary>
    /// <remarks>
    /// Body: Comment (required, non-whitespace, at most 1000 characters). An invalid comment on a Pending suggestion returns 400.
    /// Returns the reviewed suggestion with the trimmed comment, administrator's user ID and UTC review time.
    /// An unknown ID returns 404; an already reviewed suggestion returns 409 before comment validation.
    /// </remarks>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<BookSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookSuggestionDto>> Reject(Guid id, RejectBookSuggestionRequest request, CancellationToken ct) =>
        Ok(await suggestions.RejectAsync(id, request, ct));
}
