using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Staff;

/// <summary>Admin management of staff categories and borrowing limits (MEM-04).</summary>
[ApiController]
[Route("api/staff-categories")]
[Authorize(Roles = StaffRoles.LibraryAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class StaffCategoriesController(IStaffCategoryService categories) : ControllerBase
{
    /// <summary>Lists staff categories sorted by name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StaffCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffCategoryDto>>> List(CancellationToken ct)
    {
        return Ok(await categories.GetListAsync(ct));
    }

    /// <summary>Gets a staff category by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StaffCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffCategoryDto>> Get(Guid id, CancellationToken ct)
    {
        return Ok(await categories.GetAsync(id, ct));
    }

    /// <summary>Creates a staff category.</summary>
    [HttpPost]
    [ProducesResponseType<StaffCategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffCategoryDto>> Create(UpsertStaffCategoryRequest request, CancellationToken ct)
    {
        var category = await categories.CreateAsync(request, ct);

        return CreatedAtAction(
            nameof(Get),
            new { id = category.Id },
            category);
    }

    /// <summary>Updates category settings without modifying existing loan records.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StaffCategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffCategoryDto>> Update(
        Guid id,
        UpsertStaffCategoryRequest request,
        CancellationToken ct)
    {
        return Ok(await categories.UpdateAsync(id, request, ct));
    }
}