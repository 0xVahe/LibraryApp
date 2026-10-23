using LibraryApp.Api.ErrorHandling;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Reporting;

/// <summary>
/// The reports of the library (REP-01 to REP-08): read only, Librarian and Library Administrator (REP-F1); the department
/// figures are for the Library Administrator only (REP-F5). Every report states its period or date and when it was made
/// (REP-F3).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = StaffRoles.LibraryAdministrator + "," + StaffRoles.Librarian)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed partial class ReportsController(IReportingService reporting, IClock clock) : ControllerBase
{
    /// <summary>Overdue loans with the borrower, department, line manager, working days overdue and points so far (REP-01).</summary>
    /// <remarks>
    /// Librarian and Library Administrator. Open loans due before today (Asia/Yerevan), most working days overdue first;
    /// working days leave out weekends and the holidays of <c>WorkingDayCalendar:Holidays</c>, and the points are the late
    /// points a return today would add (<c>Borrowing:Penalties</c>). Out-of-range <c>page</c> and <c>pageSize</c> are
    /// corrected (page size at most 100). With <c>format=csv</c> every matching loan is in the file, not one page.
    /// <para>
    /// 400: <c>reporting.invalid_overdue_filter</c> (a negative or reversed range of working days),
    /// <c>reporting.invalid_department_id</c> (an empty id), <c>reporting.invalid_format</c> (not json or csv), or a value
    /// of the wrong type.
    /// </para>
    /// </remarks>
    /// <param name="departmentId">Only the loans of staff of this department. Left out: every department.</param>
    /// <param name="minimumDaysOverdue">Only loans at least this many working days overdue.</param>
    /// <param name="maximumDaysOverdue">Only loans at most this many working days overdue.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">Loans per page.</param>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">One page of the overdue loans, or the CSV file.</response>
    /// <response code="400">A wrong filter or format.</response>
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

    /// <summary>Dashboard: books and copies, readers, loans, overdue count and share, Restricted and Suspended staff (REP-02).</summary>
    /// <remarks>
    /// Librarian and Library Administrator. The figures of today; the answer is cached for
    /// <c>Reporting:DashboardCacheMinutes</c> (5 by default, REP-F4), so <c>generatedAtUtc</c> may be a few minutes old. The
    /// overdue share is the overdue loans out of all loans, with one decimal.
    /// <para>400: <c>reporting.invalid_format</c> (not json or csv).</para>
    /// </remarks>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The figures, or the CSV file.</response>
    /// <response code="400">A wrong format.</response>
    [HttpGet("dashboard")]
    [ProducesResponseType<DashboardReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
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

    /// <summary>The most borrowed books and categories of a period (REP-03).</summary>
    /// <remarks>
    /// Librarian and Library Administrator. Counts the loans issued in the period, most loans first, a tie by name; at most
    /// <c>Reporting:TopBooksLimit</c> books (10) and <c>Reporting:TopCategoriesLimit</c> categories (5).
    /// <para>
    /// 400: <c>reporting.invalid_date_range</c> (a missing date or <c>from</c> after <c>to</c>),
    /// <c>reporting.invalid_format</c> (not json or csv), or a date that cannot be read.
    /// </para>
    /// </remarks>
    /// <param name="from">The first day of the period (Asia/Yerevan), included.</param>
    /// <param name="to">The last day of the period (Asia/Yerevan), included.</param>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The tops of the period, or the CSV file.</response>
    /// <response code="400">A wrong period or format.</response>
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

    /// <summary>Loans of a period per department and per 10 employees, with the top category of each department (REP-04).</summary>
    /// <remarks>
    /// Library Administrator only (REP-F5). Every department is listed, also without loans; employees are the staff who have
    /// not left the bank, and a department without employees has 0 loans per 10 employees.
    /// <para>
    /// 400: <c>reporting.invalid_date_range</c> (a missing date or <c>from</c> after <c>to</c>),
    /// <c>reporting.invalid_format</c> (not json or csv), or a date that cannot be read. 403: a Librarian.
    /// </para>
    /// </remarks>
    /// <param name="from">The first day of the period (Asia/Yerevan), included.</param>
    /// <param name="to">The last day of the period (Asia/Yerevan), included.</param>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">Every department, by name, or the CSV file.</response>
    /// <response code="400">A wrong period or format.</response>
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

    /// <summary>Penalty points issued, waived and still active per department for a period (REP-06).</summary>
    /// <remarks>
    /// Library Administrator only (REP-F5). Counts the penalty records created in the period, grouped by the current
    /// department of the employee; active points are the points of those records that still count now (Active and not past
    /// their expiry). Every department is listed, also without penalties.
    /// <para>
    /// 400: <c>reporting.invalid_date_range</c> (a missing date or <c>from</c> after <c>to</c>),
    /// <c>reporting.invalid_format</c> (not json or csv), or a date that cannot be read. 403: a Librarian.
    /// </para>
    /// </remarks>
    /// <param name="from">The first day of the period (Asia/Yerevan), included.</param>
    /// <param name="to">The last day of the period (Asia/Yerevan), included.</param>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">Every department, by name, or the CSV file.</response>
    /// <response code="400">A wrong period or format.</response>
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

    /// <summary>Copies by status and condition, for the whole library or one category (REP-05).</summary>
    /// <remarks>
    /// Librarian and Library Administrator. A category includes its subcategories. Every status and condition is listed,
    /// also with 0 copies; <c>copiesInCollection</c> leaves out the Lost and Withdrawn copies.
    /// <para>
    /// 400: <c>reporting.invalid_category_id</c> (an empty id), <c>reporting.invalid_format</c> (not json or csv), or an
    /// id that cannot be read. 404: <c>reporting.category_not_found</c>.
    /// </para>
    /// </remarks>
    /// <param name="categoryId">A category. Left out: the whole library.</param>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">Every status with its copies per condition, or the CSV file.</response>
    /// <response code="400">A wrong category id or format.</response>
    /// <response code="404">No such category.</response>
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

    /// <summary>The books with the longest reservation queues: candidates to buy more copies of (REP-07).</summary>
    /// <remarks>
    /// Library Administrator only. Counts the Waiting reservations per book, longest queue first, a tie by title and then
    /// ISBN; deleted books are left out; at most <c>Reporting:PurchaseCandidatesLimit</c> books (10).
    /// <para>400: <c>reporting.invalid_format</c> (not json or csv). 403: a Librarian.</para>
    /// </remarks>
    /// <param name="format"><c>json</c> (default) or <c>csv</c>: the same report as a CSV file (REP-08).</param>
    /// <param name="ct">Cancels the request.</param>
    /// <response code="200">The books with their waiting reservations and the copies the library still has, or the CSV file.</response>
    /// <response code="400">A wrong format.</response>
    [HttpGet("purchase-candidates")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [ProducesResponseType<PurchaseCandidatesReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
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
