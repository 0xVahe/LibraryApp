using System.Globalization;
using LibraryApp.Application.Common;
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

        var rows = report.Items;

        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "LoanId", "EmployeeId", "EmployeeName", "Department", "LineManagerEmail",
            "BookTitle", "DueDate", "WorkingDaysOverdue", "PointsSoFar"
        ];
        var data = rows.Select(item => new string?[]
        {
            report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            item.LoanId.ToString(), item.EmployeeId, item.EmployeeName, item.Department,
            item.LineManagerEmail, item.BookTitle, item.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReportCsvWriter.Value(item.WorkingDaysOverdue), ReportCsvWriter.Value(item.PointsSoFar)
        });
        return Csv("overdue-loans", headers, data.DefaultIfEmpty(
            new string?[]
            {
                report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                null, null, null, null, null, null, null, null, null
            }));
    }

    /// <summary>Exports dashboard statistics as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("dashboard/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportDashboard(CancellationToken ct)
    {
        DashboardReportDto r = await reporting.GetDashboardAsync(ct);
        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "TotalBooks", "TotalCopies", "ActiveReaders", "ActiveLoans", "OverdueLoans",
            "OverduePercentage", "RestrictedEmployees", "SuspendedEmployees"
        ];
        string?[][] data =
        [
            [
                r.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ReportCsvWriter.Value(r.TotalBooks), ReportCsvWriter.Value(r.TotalCopies),
                ReportCsvWriter.Value(r.ActiveReaders), ReportCsvWriter.Value(r.ActiveLoans),
                ReportCsvWriter.Value(r.OverdueLoans), ReportCsvWriter.Value(r.OverduePercentage),
                ReportCsvWriter.Value(r.RestrictedEmployees), ReportCsvWriter.Value(r.SuspendedEmployees)
            ]
        ];
        return Csv("dashboard", headers, data);
    }

    /// <summary>Exports top books and categories as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("top-books-and-categories/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportTopBooksAndCategories(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        TopBooksAndCategoriesReportDto r = await reporting.GetTopBooksAndCategoriesAsync(from, to, ct);
        string[] headers = ["PeriodStart", "PeriodEnd", "GeneratedAtUtc", "Type", "Id", "Name", "LoanCount"];
        var books = r.TopBooks.Select(b => new string?[]
        {
            r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            "Book", b.BookId.ToString(), b.Title, ReportCsvWriter.Value(b.LoanCount)
        });
        var categories = r.TopCategories.Select(c => new string?[]
        {
            r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            "Category", c.CategoryId.ToString(), c.Name, ReportCsvWriter.Value(c.LoanCount)
        });
        return Csv("top-books-and-categories", headers, books.Concat(categories).DefaultIfEmpty(
            new string?[]
            {
                r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                null, null, null, null
            }));
    }

    /// <summary>Exports reading activity by department as CSV.</summary>
    /// <returns>A downloadable CSV report.</returns>
    [HttpGet("reading-activity-by-department/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportReadingActivityByDepartment(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        ReadingActivityByDepartmentReportDto r = await reporting.GetReadingActivityByDepartmentAsync(from, to, ct);
        string[] headers =
        [
            "PeriodStart", "PeriodEnd", "GeneratedAtUtc", "DepartmentId", "Department", "Employees", "LoanCount",
            "LoansPer10Employees", "TopCategoryId", "TopCategory", "TopCategoryLoanCount"
        ];
        var data = r.Departments.Select(d => new string?[]
        {
            r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            d.DepartmentId.ToString(), d.Department, ReportCsvWriter.Value(d.Employees),
            ReportCsvWriter.Value(d.LoanCount), ReportCsvWriter.Value(d.LoansPer10Employees),
            d.TopCategoryId?.ToString(), d.TopCategory, ReportCsvWriter.Value(d.TopCategoryLoanCount)
        });
        return Csv("reading-activity-by-department", headers, data.DefaultIfEmpty(
            new string?[]
            {
                r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                null, null, null, null, null, null, null, null
            }));
    }


    /// <summary>Exports penalties by department as CSV.</summary>
    [HttpGet("penalties/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportPenalties(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        PenaltyReportDto r = await reporting.GetPenaltiesByDepartmentAsync(from, to, ct);

        string[] headers =
        [
            "PeriodStart", "PeriodEnd", "GeneratedAtUtc", "DepartmentId",
            "Department", "Penalties", "IssuedPoints", "WaivedPoints", "ActivePoints"
        ];

        var rows = r.Departments.Select(d => new string?[]
        {
            r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            d.DepartmentId.ToString(),
            d.Department,
            ReportCsvWriter.Value(d.Penalties),
            ReportCsvWriter.Value(d.IssuedPoints),
            ReportCsvWriter.Value(d.WaivedPoints),
            ReportCsvWriter.Value(d.ActivePoints)
        });

        return Csv("penalties", headers, rows.DefaultIfEmpty(new string?[]
        {
            r.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            null, null, null, null, null, null
        }));
    }

    /// <summary>Exports inventory status as CSV.</summary>
    [HttpGet("inventory-status/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportInventoryStatus(
        [FromQuery] Guid? categoryId,
        CancellationToken ct)
    {
        InventoryStatusReportDto report =
            await reporting.GetInventoryStatusAsync(categoryId, ct);

        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "CategoryId", "Category",
            "TotalCopies", "CopiesInCollection", "Status",
            "StatusCopies", "Condition", "ConditionCopies"
        ];

        var rows = report.Statuses.SelectMany(status =>
            status.Conditions.Select(condition => new string?[]
            {
                report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                report.CategoryId?.ToString(),
                report.Category,
                ReportCsvWriter.Value(report.TotalCopies),
                ReportCsvWriter.Value(report.CopiesInCollection),
                status.Status.ToString(),
                ReportCsvWriter.Value(status.Copies),
                condition.Condition.ToString(),
                ReportCsvWriter.Value(condition.Copies)
            }));

        return Csv("inventory-status", headers, rows.DefaultIfEmpty(
            new string?[]
            {
                report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                report.CategoryId?.ToString(),
                report.Category,
                ReportCsvWriter.Value(report.TotalCopies),
                ReportCsvWriter.Value(report.CopiesInCollection),
                null, null, null, null
            }));
    }

    /// <summary>Exports purchase candidates as CSV.</summary>
    [HttpGet("purchase-candidates/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPurchaseCandidates(CancellationToken ct)
    {
        PurchaseCandidatesReportDto report =
            await reporting.GetPurchaseCandidatesAsync(ct);

        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "Limit", "BookId",
            "Title", "Isbn", "Category", "WaitingReservations",
            "CopiesInCollection"
        ];

        var rows = report.Books.Select(book => new string?[]
        {
            report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ReportCsvWriter.Value(report.Limit),
            book.BookId.ToString(),
            book.Title,
            book.Isbn,
            book.Category,
            ReportCsvWriter.Value(book.WaitingReservations),
            ReportCsvWriter.Value(book.CopiesInCollection)
        });

        return Csv("purchase-candidates", headers, rows.DefaultIfEmpty(
            new string?[]
            {
                report.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ReportCsvWriter.Value(report.Limit),
                null, null, null, null, null, null
            }));
    }

    private FileContentResult Csv(string name, string[] headers, IEnumerable<string?[]> rows)
    {
        Response.Headers.CacheControl = "no-store";
        return File(ReportCsvWriter.Write(headers, rows), "text/csv; charset=utf-8",
            $"{name}-{clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv");
    }
}
