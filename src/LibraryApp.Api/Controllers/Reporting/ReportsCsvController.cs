using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Reporting;

public sealed partial class ReportsController
{
    /// <summary>Exports overdue loans as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("overdue-loans/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportOverdueLoans(
        [FromQuery] Guid? departmentId,
        [FromQuery] int? minimumDaysOverdue,
        [FromQuery] int? maximumDaysOverdue,
        CancellationToken ct)
    {
        OverdueLoansReportDto report = await reporting.GetAllOverdueLoansAsync(
            departmentId, minimumDaysOverdue, maximumDaysOverdue, ct);
        return Csv("overdue-loans", ReportCsv.OverdueLoans(report));
    }

    /// <summary>Exports dashboard statistics as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("dashboard/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportDashboard(CancellationToken ct) =>
        Csv("dashboard", ReportCsv.Dashboard(await reporting.GetDashboardAsync(ct)));

    /// <summary>Exports top books and categories as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("top-books-and-categories/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportTopBooksAndCategories(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Csv("top-books-and-categories", ReportCsv.TopBooksAndCategories(await reporting.GetTopBooksAndCategoriesAsync(from, to, ct)));

    /// <summary>Exports reading activity by department as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("reading-activity-by-department/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportReadingActivityByDepartment(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Csv("reading-activity-by-department",
            ReportCsv.ReadingActivityByDepartment(await reporting.GetReadingActivityByDepartmentAsync(from, to, ct)));

    /// <summary>Exports penalties by department as CSV.</summary>
    [HttpGet("penalties/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportPenalties(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct) =>
        Csv("penalties", ReportCsv.Penalties(await reporting.GetPenaltiesByDepartmentAsync(from, to, ct)));

    /// <summary>Exports inventory status as CSV.</summary>
    [HttpGet("inventory-status/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportInventoryStatus(
        [FromQuery] Guid? categoryId,
        CancellationToken ct) =>
        Csv("inventory-status", ReportCsv.InventoryStatus(await reporting.GetInventoryStatusAsync(categoryId, ct)));

    /// <summary>Exports purchase candidates as CSV.</summary>
    [HttpGet("purchase-candidates/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPurchaseCandidates(CancellationToken ct) =>
        Csv("purchase-candidates", ReportCsv.PurchaseCandidates(await reporting.GetPurchaseCandidatesAsync(ct)));

    private FileContentResult Csv(string name, byte[] content)
    {
        Response.Headers.CacheControl = "no-store";
        return File(content, ReportCsv.ContentType, ReportCsv.FileName(name, clock.UtcNow));
    }
}
