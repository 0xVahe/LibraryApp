using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

/// <summary>Catalog publishers. Reads: any signed-in user; writes: Librarian or Administrator.</summary>
[ApiController]
[Route("api/publishers")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class PublishersController(IPublisherService publishers) : ControllerBase
{
    private const string Writers = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>Lists publishers.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PublisherDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PublisherDto>>> List(CancellationToken ct) => Ok(await publishers.ListAsync(ct));

    /// <summary>Gets a publisher.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PublisherDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublisherDto>> Get(Guid id, CancellationToken ct) => Ok(await publishers.GetAsync(id, ct));

    /// <summary>Creates a publisher.</summary>
    [HttpPost]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<PublisherDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PublisherDto>> Create(PublisherRequest request, CancellationToken ct)
    {
        var publisher = await publishers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = publisher.Id }, publisher);
    }

    /// <summary>Renames a publisher.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, PublisherRequest request, CancellationToken ct)
    {
        await publishers.UpdateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Deletes a publisher only when no book references it, deleted books included.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await publishers.DeleteAsync(id, ct);
        return NoContent();
    }
}
