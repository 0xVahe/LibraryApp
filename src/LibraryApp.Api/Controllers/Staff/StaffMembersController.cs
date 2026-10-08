using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Staff;

[ApiController]
[Tags("Staff Members")]
[Route("api/staff-members")]
[Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
public sealed class StaffMembersController(IStaffManagementService staff) : ControllerBase
{
    /// <summary>Enrolls an employee and creates their login account (MEM-01, MEM-03, BR-MEM-1, BR-MEM-5).</summary>
    /// <remarks>Role defaults to StaffMember. Only LibraryAdministrator may assign Librarian or LibraryAdministrator.</remarks>
    [HttpPost]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffMemberDto>> Enrol(EnrolStaffMemberRequest request, CancellationToken ct)
    {
        var result = await staff.EnrolAsync(request, User.IsInRole(StaffRoles.LibraryAdministrator), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>Returns a staff profile with department, category and role (MEM-01).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffMemberDto>> Get(Guid id, CancellationToken ct) => Ok(await staff.GetAsync(id, ct));

    /// <summary>Updates a staff profile and synchronizes the account's email and username (MEM-01, MEM-03).</summary>
    /// <remarks>EmployeeId and role are immutable here. Deactivated profiles cannot be updated.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffMemberDto>> Update(Guid id, UpdateStaffMemberRequest request, CancellationToken ct) =>
        Ok(await staff.UpdateAsync(id, request, ct));
}
