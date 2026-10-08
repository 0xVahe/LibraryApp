using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

/// <summary>Two-level categories (CAT-03). Reads: any signed-in user; writes: Librarian or Administrator.</summary>
[ApiController]
[Route("api/categories")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    private const string Writers = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian;

    /// <summary>Lists categories and their optional parent IDs (CAT-03).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct) => Ok(await categories.ListAsync(ct));

    /// <summary>Gets a category (CAT-03).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> Get(Guid id, CancellationToken ct) => Ok(await categories.GetAsync(id, ct));

    /// <summary>Creates a top-level category, or a subcategory of a top-level category (CAT-03).</summary>
    [HttpPost]
    [Authorize(Roles = Writers)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    /// <summary>Renames or moves a category, keeping at most two levels (CAT-03).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, CategoryRequest request, CancellationToken ct)
    {
        await categories.UpdateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Deletes a category only when no book (deleted ones included) or subcategory references it (CAT-03).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Writers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}
