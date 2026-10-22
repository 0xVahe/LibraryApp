using LibraryApp.Application.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Controllers.Reporting;

public sealed partial class ReportsController
{
    [HttpGet("overdue-loans/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportOverdueLoans(
        [FromQuery] Guid? departmentId,
        [FromQuery] int? minimumDaysOverdue,
        [FromQuery] int? maximumDaysOverdue,
        CancellationToken ct)
    {
        var rows = new List<OverdueLoanItemDto>();
        int page = 1;
        OverdueLoansReportDto report;
        do
        {
            report = await reporting.GetOverdueLoansAsync(
                departmentId, minimumDaysOverdue, maximumDaysOverdue,
                new PageRequest(page, PageRequest.MaxPageSize), ct);
            rows.AddRange(report.Items);
            page++;
        } while (rows.Count < report.TotalCount);

        string[] headers = ["AsOfDate", "GeneratedAtUtc", "LoanId", "EmployeeId", "EmployeeName", "Department", "LineManagerEmail", "BookTitle", "DueDate", "WorkingDaysOverdue", "PointsSoFar"];
        var data = rows.Select(item => new string?[]
        {
            report.AsOfDate.ToString("yyyy-MM-dd"), report.GeneratedAtUtc.ToString("O"),
            item.LoanId.ToString(), item.EmployeeId, item.EmployeeName, item.Department,
            item.LineManagerEmail, item.BookTitle, item.DueDate.ToString("yyyy-MM-dd"),
            ReportCsvWriter.Value(item.WorkingDaysOverdue), ReportCsvWriter.Value(item.PointsSoFar)
        });
        return Csv("overdue-loans", headers, data);
    }

    [HttpGet("dashboard/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportDashboard(CancellationToken ct)
    {
        DashboardReportDto r = await reporting.GetDashboardAsync(ct);
        string[] headers = ["AsOfDate", "GeneratedAtUtc", "TotalBooks", "TotalCopies", "ActiveReaders", "ActiveLoans", "OverdueLoans", "OverduePercentage", "RestrictedEmployees", "SuspendedEmployees"];
        string?[][] data = [[r.AsOfDate.ToString("yyyy-MM-dd"), r.GeneratedAtUtc.ToString("O"),
            ReportCsvWriter.Value(r.TotalBooks), ReportCsvWriter.Value(r.TotalCopies),
            ReportCsvWriter.Value(r.ActiveReaders), ReportCsvWriter.Value(r.ActiveLoans),
            ReportCsvWriter.Value(r.OverdueLoans), ReportCsvWriter.Value(r.OverduePercentage),
            ReportCsvWriter.Value(r.RestrictedEmployees), ReportCsvWriter.Value(r.SuspendedEmployees)]];
        return Csv("dashboard", headers, data);
    }

    [HttpGet("top-books-and-categories/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportTopBooksAndCategories(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        TopBooksAndCategoriesReportDto r = await reporting.GetTopBooksAndCategoriesAsync(from, to, ct);
        string[] headers = ["PeriodStart", "PeriodEnd", "GeneratedAtUtc", "Type", "Id", "Name", "LoanCount"];
        var books = r.TopBooks.Select(b => new string?[] { r.PeriodStart.ToString("yyyy-MM-dd"), r.PeriodEnd.ToString("yyyy-MM-dd"), r.GeneratedAtUtc.ToString("O"), "Book", b.BookId.ToString(), b.Title, ReportCsvWriter.Value(b.LoanCount) });
        var categories = r.TopCategories.Select(c => new string?[] { r.PeriodStart.ToString("yyyy-MM-dd"), r.PeriodEnd.ToString("yyyy-MM-dd"), r.GeneratedAtUtc.ToString("O"), "Category", c.CategoryId.ToString(), c.Name, ReportCsvWriter.Value(c.LoanCount) });
        return Csv("top-books-and-categories", headers, books.Concat(categories));
    }

    [HttpGet("reading-activity-by-department/export")]
    [Authorize(Roles = StaffRoles.LibraryAdministrator)]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportReadingActivityByDepartment(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        ReadingActivityByDepartmentReportDto r = await reporting.GetReadingActivityByDepartmentAsync(from, to, ct);
        string[] headers = ["PeriodStart", "PeriodEnd", "GeneratedAtUtc", "DepartmentId", "Department", "Employees", "LoanCount", "LoansPer10Employees", "TopCategoryId", "TopCategory", "TopCategoryLoanCount"];
        var data = r.Departments.Select(d => new string?[]
        {
            r.PeriodStart.ToString("yyyy-MM-dd"), r.PeriodEnd.ToString("yyyy-MM-dd"), r.GeneratedAtUtc.ToString("O"),
            d.DepartmentId.ToString(), d.Department, ReportCsvWriter.Value(d.Employees),
            ReportCsvWriter.Value(d.LoanCount), ReportCsvWriter.Value(d.LoansPer10Employees),
            d.TopCategoryId?.ToString(), d.TopCategory, ReportCsvWriter.Value(d.TopCategoryLoanCount)
        });
        return Csv("reading-activity-by-department", headers, data);
    }

    private FileContentResult Csv(string name, string[] headers, IEnumerable<string?[]> rows) =>
        File(ReportCsvWriter.Write(headers, rows), "text/csv; charset=utf-8", $"{name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
}
