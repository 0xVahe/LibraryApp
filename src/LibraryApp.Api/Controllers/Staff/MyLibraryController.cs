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
}