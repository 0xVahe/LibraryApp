using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LibraryApp.Application.Common;

namespace LibraryApp.Api.Controllers.Staff;

[ApiController]
[Tags("Staff Members")]
[Route("api/staff-members")]
[Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
public sealed class StaffMembersController(
    IStaffManagementService staff,
    IStaffDeactivationService deactivation,
    IStaffImportService import) : ControllerBase
{
    /// <summary>Imports up to 200 staff members from a CSV file (MEM-09). All rows are saved together or none are.</summary>
    /// <remarks>
    /// Upload multipart/form-data using the <c>file</c> field. A successful response contains each member and a
    /// one-time temporary password. Imported accounts must change that password at their first sign-in.
    /// </remarks>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(1_100_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_100_000)]
    [ProducesResponseType<StaffImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StaffImportResult>> Import(IFormFile file, CancellationToken ct)
    {
        if (file.Length > StaffImportLimits.MaximumFileBytes)
            throw ImportFailure([new StaffImportError(1, "File", "The file must be 1 MB or smaller.")]);

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await import.ImportAsync(stream, ct));
        }
        catch (StaffImportException ex)
        {
            throw ImportFailure(ex.Errors);
        }
        catch (StaffCsvFormatException ex)
        {
            throw ImportFailure([new StaffImportError(ex.RowNumber, ex.Field, ex.Message)]);
        }
    }

    private static DomainException ImportFailure(IReadOnlyList<StaffImportError> rows) =>
        new(StaffErrors.ImportInvalid, extensions: new Dictionary<string, object?> { ["rows"] = rows });

    /// <summary>Deactivates an employee who has no outstanding loans (MEM-01, BR-MEM-4).</summary>
    /// <remarks>A 409 staff.has_active_loans response lists titles and barcodes in outstandingBooks.
    /// Success locks the login account and publishes StaffStatusChanged. Repeating deactivation returns 422.</remarks>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Deactivate(Guid id, DeactivateStaffMemberRequest request, CancellationToken ct)
    {
        await deactivation.DeactivateAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Searches staff with pagination (MEM-07).</summary>
    /// <remarks>
    /// Results are ordered by last name, first name, then ID.
    /// Filters combine using AND.
    /// </remarks>
    /// <param name="search">Prefix of first name, last name, full name or employee ID.</param>
    /// <param name="departmentId">Department to filter by.</param>
    /// <param name="status">Active, Restricted, Suspended or Deactivated; case insensitive.</param>
    /// <param name="page">Page number, starting from 1.</param>
    /// <param name="pageSize">Items per page: default 20, maximum 100.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<StaffMemberListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<StaffMemberListItemDto>>> Search(
        [FromQuery] string? search = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default)
    {
        var request = new StaffMemberSearchRequest(search, departmentId, status, new PageRequest(page, pageSize));
        var result = await staff.SearchAsync(request, ct);
        return Ok(result);
    }

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
        var result = await staff.EnrolAsync(request, ct);
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
    /// <remarks>EmployeeId and role are immutable here. Deactivated profiles cannot be updated; this returns 422 staff.deactivated_profile_locked.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<StaffMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StaffMemberDto>> Update(Guid id, UpdateStaffMemberRequest request, CancellationToken ct) =>
        Ok(await staff.UpdateAsync(id, request, ct));
}
