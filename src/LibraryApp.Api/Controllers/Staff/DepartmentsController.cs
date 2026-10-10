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
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await departmentService.GetAsync(id, ct));

    /// <summary>
    /// Create a department with a unique name (MEM-06)
    /// </summary>
    [HttpPost]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentDto>> Create(UpsertDepartmentRequest data, CancellationToken ct)
    {
        var department = await departmentService.CreateAsync(data, ct);
        return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
    }

    /// <summary>
    /// Rename a department and return it (MEM-06)
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentDto>> Update(Guid id, UpsertDepartmentRequest data, CancellationToken ct) =>
        Ok(await departmentService.UpdateAsync(id, data, ct));
}