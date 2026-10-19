using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Staff;

/// <summary>
/// Own controller: StaffMembersController allows only Admin and Librarian at class level, and a method-level
/// [Authorize] there would be added to that rule, not replace it.
/// </summary>
[ApiController]
[Tags("Staff Members")]
[Route("api/staff-members/me")]
[Authorize(Roles = StaffRoles.StaffMember)]
public sealed class MyLibraryController(IMyLibraryService myLibrary) : ControllerBase
{
    /// <summary>My library: the caller's profile, active loans, reservations and active penalty points (MEM-08).</summary>
    /// <remarks>
    /// The member comes from the access token (staff_member_id); the route takes no id, so a caller only ever sees
    /// their own data (NFR-03). StaffMember role only. 404 staff.not_found when the token has no staff profile.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<MyLibraryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MyLibraryDto>> Get(CancellationToken ct) => Ok(await myLibrary.GetAsync(ct));

    /// <summary>Updates the caller's own first and last name (S2-01).</summary>
    /// <remarks>
    /// S2-01 extends the read-only MEM-08 "My library" profile. The member is taken from the access token; only users
    /// with the StaffMember role may use this endpoint. Librarians and Library Administrators use the HR-managed
    /// staff update endpoint. Email, department, category, position, role, status and EmployeeId are unchanged.
    /// </remarks>
    [HttpPut]
    [ProducesResponseType<MyProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MyProfileDto>> UpdateProfile(UpdateMyProfileRequest request, CancellationToken ct) =>
        Ok(await myLibrary.UpdateProfileAsync(request, ct));
}
