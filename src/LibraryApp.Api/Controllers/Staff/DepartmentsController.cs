using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Staff;

[ApiController]
[Tags("Departments")]
[Route("api/departments")]
[Authorize(Roles = StaffRoles.LibraryAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class DepartmentsController(IDepartmentService departmentService) : ControllerBase
{
    /// <summary>
    /// Returns department list by name (MEM-06)
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DepartmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> List(CancellationToken ct) =>
        Ok(await departmentService.ListAsync(ct));

    /// <summary>
    /// Gets a department (MEM-06)
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await departmentService.GetAsync(id, ct));

    /// <summary>
    /// Create a department (MEM-06)
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DepartmentDto>> Create(UpsertDepartmentRequest data, CancellationToken ct)
    {
        var department = await departmentService.CreateAsync(data, ct);
        return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
    }

    /// <summary>
    /// Update a department (MEM-06)
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpsertDepartmentRequest data, CancellationToken ct) =>
        Ok(await departmentService.UpdateAsync(id, data, ct));
}