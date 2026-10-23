using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Reporting;

/// <summary>The CSV files of the reports (REP-08); the same files as <c>?format=csv</c> on the report routes.</summary>
public sealed partial class ReportsController
{
    /// <summary>Every overdue loan that matches the filters as CSV (REP-08, REP-01).</summary>
    /// <remarks>
    /// Librarian and Library Administrator. Not paged: every matching loan is in the file.
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// <para>
    /// 400: <c>reporting.invalid_overdue_filter</c>, <c>reporting.invalid_department_id</c>, or a value of the wrong type.
    /// </para>
    /// </remarks>
    /// <param name="departmentId">Only the loans of staff of this department.</param>
    /// <param name="minimumDaysOverdue">Only loans at least this many working days overdue.</param>
    /// <param name="maximumDaysOverdue">Only loans at most this many working days overdue.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">A wrong filter.</response>
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

    /// <summary>The dashboard as CSV, one row (REP-08, REP-02).</summary>
    /// <remarks>
    /// Librarian and Library Administrator. The same cached figures as the dashboard.
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// </remarks>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
    [HttpGet("dashboard/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportDashboard(CancellationToken ct) =>
        Csv("dashboard", ReportCsv.Dashboard(await reporting.GetDashboardAsync(ct)));

    /// <summary>The top books and categories of a period as CSV, one row each with its type (REP-08, REP-03).</summary>
    /// <remarks>
    /// Librarian and Library Administrator.
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// <para>400: <c>reporting.invalid_date_range</c>, or a date that cannot be read.</para>
    /// </remarks>
    /// <param name="from">The first day of the period (Asia/Yerevan), included.</param>
    /// <param name="to">The last day of the period (Asia/Yerevan), included.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">A wrong period.</response>
    [HttpGet("top-books-and-categories/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportTopBooksAndCategories(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Csv("top-books-and-categories", ReportCsv.TopBooksAndCategories(await reporting.GetTopBooksAndCategoriesAsync(from, to, ct)));

    /// <summary>Reading activity by department as CSV (REP-08, REP-04).</summary>
    /// <remarks>
    /// Library Administrator only (REP-F5).
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// <para>400: <c>reporting.invalid_date_range</c>, or a date that cannot be read. 403: a Librarian.</para>
    /// </remarks>
    /// <param name="from">The first day of the period (Asia/Yerevan), included.</param>
    /// <param name="to">The last day of the period (Asia/Yerevan), included.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">A wrong period.</response>
    [HttpGet("reading-activity-by-department/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportReadingActivityByDepartment(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Csv("reading-activity-by-department",
            ReportCsv.ReadingActivityByDepartment(await reporting.GetReadingActivityByDepartmentAsync(from, to, ct)));

    /// <summary>Penalty points by department as CSV (REP-08, REP-06).</summary>
    /// <remarks>
    /// Library Administrator only (REP-F5).
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// <para>400: <c>reporting.invalid_date_range</c>, or a date that cannot be read. 403: a Librarian.</para>
    /// </remarks>
    /// <param name="from">The first day of the period (Asia/Yerevan), included.</param>
    /// <param name="to">The last day of the period (Asia/Yerevan), included.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">A wrong period.</response>
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

    /// <summary>Inventory status as CSV, one row per status and condition (REP-08, REP-05).</summary>
    /// <remarks>
    /// Librarian and Library Administrator.
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// <para>
    /// 400: <c>reporting.invalid_category_id</c>, or an id that cannot be read. 404: <c>reporting.category_not_found</c>.
    /// </para>
    /// </remarks>
    /// <param name="categoryId">A category; its subcategories are included. Left out: the whole library.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">A wrong category id.</response>
    /// <response code="404">No such category.</response>
    [HttpGet("inventory-status/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportInventoryStatus(
        [FromQuery] Guid? categoryId,
        CancellationToken ct) =>
        Csv("inventory-status", ReportCsv.InventoryStatus(await reporting.GetInventoryStatusAsync(categoryId, ct)));

    /// <summary>Purchase candidates as CSV (REP-08, REP-07).</summary>
    /// <remarks>
    /// Library Administrator only.
    /// The file is UTF-8 with a header row; every row also carries the period or date and the generation time, and an
    /// empty report has one such row. Values that a spreadsheet would run as a formula start with an apostrophe.
    /// <para>403: a Librarian.</para>
    /// </remarks>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The CSV file.</response>
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
