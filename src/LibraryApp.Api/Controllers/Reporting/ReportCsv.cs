using System.Globalization;
using LibraryApp.Application.Reporting.Dto;

namespace LibraryApp.Api.Controllers.Reporting;

/// <summary>
/// The CSV files of the reports (REP-08), shared by the API exports and the Reports pages. Every row carries the period or
/// the date and the generation time (REP-F3); an empty report still has one row with them.
/// </summary>
internal static class ReportCsv
{
    public const string ContentType = "text/csv; charset=utf-8";

    public static string FileName(string report, DateTime utcNow) =>
        $"{report}-{utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv";

    public static byte[] OverdueLoans(OverdueLoansReportDto report)
    {
        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "LoanId", "EmployeeId", "EmployeeName", "Department", "LineManagerEmail",
            "BookTitle", "DueDate", "WorkingDaysOverdue", "PointsSoFar"
        ];
        var rows = report.Items.Select(item => new string?[]
        {
            Date(report.AsOfDate), Time(report.GeneratedAtUtc),
            item.LoanId.ToString(), item.EmployeeId, item.EmployeeName, item.Department,
            item.LineManagerEmail, item.BookTitle, Date(item.DueDate),
            ReportCsvWriter.Value(item.WorkingDaysOverdue), ReportCsvWriter.Value(item.PointsSoFar)
        });
        return ReportCsvWriter.Write(headers, rows.DefaultIfEmpty(
            [Date(report.AsOfDate), Time(report.GeneratedAtUtc), null, null, null, null, null, null, null, null, null]));
    }

    public static byte[] Dashboard(DashboardReportDto r)
    {
        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "TotalBooks", "TotalCopies", "ActiveReaders", "ActiveLoans", "OverdueLoans",
            "OverduePercentage", "RestrictedEmployees", "SuspendedEmployees"
        ];
        string?[][] rows =
        [
            [
                Date(r.AsOfDate), Time(r.GeneratedAtUtc),
                ReportCsvWriter.Value(r.TotalBooks), ReportCsvWriter.Value(r.TotalCopies),
                ReportCsvWriter.Value(r.ActiveReaders), ReportCsvWriter.Value(r.ActiveLoans),
                ReportCsvWriter.Value(r.OverdueLoans), ReportCsvWriter.Value(r.OverduePercentage),
                ReportCsvWriter.Value(r.RestrictedEmployees), ReportCsvWriter.Value(r.SuspendedEmployees)
            ]
        ];
        return ReportCsvWriter.Write(headers, rows);
    }

    public static byte[] TopBooksAndCategories(TopBooksAndCategoriesReportDto r)
    {
        string[] headers = ["PeriodStart", "PeriodEnd", "GeneratedAtUtc", "Type", "Id", "Name", "LoanCount"];
        var books = r.TopBooks.Select(b => new string?[]
        {
            Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc),
            "Book", b.BookId.ToString(), b.Title, ReportCsvWriter.Value(b.LoanCount)
        });
        var categories = r.TopCategories.Select(c => new string?[]
        {
            Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc),
            "Category", c.CategoryId.ToString(), c.Name, ReportCsvWriter.Value(c.LoanCount)
        });
        return ReportCsvWriter.Write(headers, books.Concat(categories).DefaultIfEmpty(
            [Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc), null, null, null, null]));
    }

    public static byte[] ReadingActivityByDepartment(ReadingActivityByDepartmentReportDto r)
    {
        string[] headers =
        [
            "PeriodStart", "PeriodEnd", "GeneratedAtUtc", "DepartmentId", "Department", "Employees", "LoanCount",
            "LoansPer10Employees", "TopCategoryId", "TopCategory", "TopCategoryLoanCount"
        ];
        var rows = r.Departments.Select(d => new string?[]
        {
            Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc),
            d.DepartmentId.ToString(), d.Department, ReportCsvWriter.Value(d.Employees),
            ReportCsvWriter.Value(d.LoanCount), ReportCsvWriter.Value(d.LoansPer10Employees),
            d.TopCategoryId?.ToString(), d.TopCategory, ReportCsvWriter.Value(d.TopCategoryLoanCount)
        });
        return ReportCsvWriter.Write(headers, rows.DefaultIfEmpty(
            [Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc), null, null, null, null, null, null, null, null]));
    }

    public static byte[] Penalties(PenaltyReportDto r)
    {
        string[] headers =
        [
            "PeriodStart", "PeriodEnd", "GeneratedAtUtc", "DepartmentId",
            "Department", "Penalties", "IssuedPoints", "WaivedPoints", "ActivePoints"
        ];
        var rows = r.Departments.Select(d => new string?[]
        {
            Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc),
            d.DepartmentId.ToString(), d.Department,
            ReportCsvWriter.Value(d.Penalties), ReportCsvWriter.Value(d.IssuedPoints),
            ReportCsvWriter.Value(d.WaivedPoints), ReportCsvWriter.Value(d.ActivePoints)
        });
        return ReportCsvWriter.Write(headers, rows.DefaultIfEmpty(
            [Date(r.PeriodStart), Date(r.PeriodEnd), Time(r.GeneratedAtUtc), null, null, null, null, null, null]));
    }

    public static byte[] InventoryStatus(InventoryStatusReportDto report)
    {
        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "CategoryId", "Category",
            "TotalCopies", "CopiesInCollection", "Status",
            "StatusCopies", "Condition", "ConditionCopies"
        ];
        var rows = report.Statuses.SelectMany(status =>
            status.Conditions.Select(condition => new string?[]
            {
                Date(report.AsOfDate), Time(report.GeneratedAtUtc),
                report.CategoryId?.ToString(), report.Category,
                ReportCsvWriter.Value(report.TotalCopies), ReportCsvWriter.Value(report.CopiesInCollection),
                status.Status.ToString(), ReportCsvWriter.Value(status.Copies),
                condition.Condition.ToString(), ReportCsvWriter.Value(condition.Copies)
            }));
        return ReportCsvWriter.Write(headers, rows.DefaultIfEmpty(
        [
            Date(report.AsOfDate), Time(report.GeneratedAtUtc), report.CategoryId?.ToString(), report.Category,
            ReportCsvWriter.Value(report.TotalCopies), ReportCsvWriter.Value(report.CopiesInCollection),
            null, null, null, null
        ]));
    }

    public static byte[] PurchaseCandidates(PurchaseCandidatesReportDto report)
    {
        string[] headers =
        [
            "AsOfDate", "GeneratedAtUtc", "Limit", "BookId",
            "Title", "Isbn", "Category", "WaitingReservations",
            "CopiesInCollection"
        ];
        var rows = report.Books.Select(book => new string?[]
        {
            Date(report.AsOfDate), Time(report.GeneratedAtUtc), ReportCsvWriter.Value(report.Limit),
            book.BookId.ToString(), book.Title, book.Isbn, book.Category,
            ReportCsvWriter.Value(book.WaitingReservations), ReportCsvWriter.Value(book.CopiesInCollection)
        });
        return ReportCsvWriter.Write(headers, rows.DefaultIfEmpty(
            [Date(report.AsOfDate), Time(report.GeneratedAtUtc), ReportCsvWriter.Value(report.Limit), null, null, null, null, null, null]));
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Time(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);
}
