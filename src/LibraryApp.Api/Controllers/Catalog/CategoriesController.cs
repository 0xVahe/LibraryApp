using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Catalog;

[ApiController]
[Route("api/categories")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    /// <summary>Lists categories and their optional parent IDs (CAT-03).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken ct) => Ok(await categories.ListAsync(ct));

    /// <summary>Gets a category (CAT-03).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Get(Guid id, CancellationToken ct) => Ok(await categories.GetAsync(id, ct));

    /// <summary>Creates a root category or a child of a root (CAT-03).</summary>
    [HttpPost]
    [Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    /// <summary>Renames or reparents a category, retaining at most two levels (CAT-03).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, CategoryRequest request, CancellationToken ct)
    {
        await categories.UpdateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Deletes a category only when no book or subcategory references it (CAT-03).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}
