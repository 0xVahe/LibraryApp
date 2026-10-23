using LibraryApp.Api.ErrorHandling;
using LibraryApp.Application.Abstractions.Common;
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
public sealed partial class ReportsController(IReportingService reporting, IClock clock) : ControllerBase
{
    [HttpGet("overdue-loans")]
    [ProducesResponseType<OverdueLoansReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOverdueLoans(
        [FromQuery] Guid? departmentId,
        [FromQuery] int? minimumDaysOverdue,
        [FromQuery] int? maximumDaysOverdue,
	[FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportOverdueLoans(departmentId, minimumDaysOverdue, maximumDaysOverdue, ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));


        return Ok(await reporting.GetOverdueLoansAsync(departmentId, minimumDaysOverdue, maximumDaysOverdue, new PageRequest(page, pageSize), ct));
    }

    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardReportDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard([FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportDashboard(ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));

        return Ok(await reporting.GetDashboardAsync(ct));
    }

    [HttpGet("top-books-and-categories")]
    [ProducesResponseType<TopBooksAndCategoriesReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTopBooksAndCategories(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportTopBooksAndCategories(from, to, ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));


        return Ok(await reporting.GetTopBooksAndCategoriesAsync(from, to, ct));
    }

    [HttpGet("reading-activity-by-department")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<ReadingActivityByDepartmentReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReadingActivityByDepartment(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportReadingActivityByDepartment(from, to, ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));


        return Ok(await reporting.GetReadingActivityByDepartmentAsync(from, to, ct));
    }

    [HttpGet("penalties")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<PenaltyReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPenalties(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string format = "json",
        CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportPenalties(from, to, ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));

        return Ok(await reporting.GetPenaltiesByDepartmentAsync(from, to, ct));
    }

    /// <summary>REP-05: copies by status and condition, for the whole library or one category.</summary>
    /// <param name="format">Response format: json or csv.</param>
    /// <param name="categoryId">A category; its subcategories are included. Left out: the whole library.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">Every status with its copies per condition, also with 0 copies.</response>
    /// <response code="400"><c>reporting.invalid_category_id</c>: an empty id.</response>
    /// <response code="404"><c>reporting.category_not_found</c>: no such category.</response>
    [HttpGet("inventory-status")]
    [ProducesResponseType<InventoryStatusReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInventoryStatus(
        [FromQuery] Guid? categoryId,
        [FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportInventoryStatus(categoryId, ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));


        return Ok(await reporting.GetInventoryStatusAsync(categoryId, ct));
    }

    /// <summary>
    /// REP-07: the books with the longest reservation queues, longest first; at most
    /// <c>Reporting:PurchaseCandidatesLimit</c>. Administrator only.
    /// </summary>
    /// <param name="format">Response format: json or csv.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The books with their waiting reservations and the copies the library still has.</response>
    [HttpGet("purchase-candidates")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<PurchaseCandidatesReportDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPurchaseCandidates([FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return await ExportPurchaseCandidates(ct);

        if (!string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(GlobalExceptionHandler.Problem(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "Unsupported report format. Use json or csv.",
                "reporting.invalid_format"));

        return Ok(await reporting.GetPurchaseCandidatesAsync(ct));
    }
}
