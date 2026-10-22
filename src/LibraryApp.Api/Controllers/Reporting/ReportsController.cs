using LibraryApp.Application.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Reporting;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ReportsController(IReportingService reporting) : ControllerBase
{
    [HttpGet("overdue-loans")]
    [ProducesResponseType<OverdueLoansReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OverdueLoansReportDto>> GetOverdueLoans(
        [FromQuery] Guid? departmentId,
        [FromQuery] int? minimumDaysOverdue,
        [FromQuery] int? maximumDaysOverdue,
	[FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken ct = default)
    {
        return Ok(await reporting.GetOverdueLoansAsync(departmentId, minimumDaysOverdue, maximumDaysOverdue, new PageRequest(page, pageSize), ct));
    }

    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardReportDto>> GetDashboard(CancellationToken ct) =>
        Ok(await reporting.GetDashboardAsync(ct));

    [HttpGet("top-books-and-categories")]
    [ProducesResponseType<TopBooksAndCategoriesReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TopBooksAndCategoriesReportDto>> GetTopBooksAndCategories(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        return Ok(await reporting.GetTopBooksAndCategoriesAsync(from, to, ct));
    }

    [HttpGet("reading-activity-by-department")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<ReadingActivityByDepartmentReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReadingActivityByDepartmentReportDto>> GetReadingActivityByDepartment(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        return Ok(await reporting.GetReadingActivityByDepartmentAsync(from, to, ct));
    }

    /// <summary>REP-05: copies by status and condition, for the whole library or one category.</summary>
    /// <param name="categoryId">A category; its subcategories are included. Empty: the whole library.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">Every status with its copies per condition, also with 0 copies.</response>
    /// <response code="400"><c>reporting.invalid_category_id</c>: an empty id.</response>
    /// <response code="404"><c>reporting.category_not_found</c>: no such category.</response>
    [HttpGet("inventory-status")]
    [ProducesResponseType<InventoryStatusReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryStatusReportDto>> GetInventoryStatus(
        [FromQuery] Guid? categoryId,
        CancellationToken ct)
    {
        return Ok(await reporting.GetInventoryStatusAsync(categoryId, ct));
    }
}
