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
        CancellationToken ct)
    {
        if (minimumDaysOverdue is < 0 || maximumDaysOverdue is < 0 ||
            (minimumDaysOverdue.HasValue && maximumDaysOverdue.HasValue && minimumDaysOverdue > maximumDaysOverdue) ||
            departmentId == Guid.Empty)
            return BadRequest(new ProblemDetails { Title = "Invalid report filters", Status = StatusCodes.Status400BadRequest });

        return Ok(await reporting.GetOverdueLoansAsync(departmentId, minimumDaysOverdue, maximumDaysOverdue, ct));
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
        if (from == default || to == default || from > to)
            return BadRequest(new ProblemDetails { Title = "A valid date range is required", Status = StatusCodes.Status400BadRequest });

        return Ok(await reporting.GetTopBooksAndCategoriesAsync(from, to, ct));
    }
}
