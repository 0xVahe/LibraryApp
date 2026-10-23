using System.Globalization;
using LibraryApp.Api.Controllers.Reporting;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>
/// The reports of the library (REP-F1): Librarian and Library Administrator only. Read only; every report states its
/// period or date and when it was generated (REP-F3).
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class IndexModel(
    IReportingService reports,
    IDepartmentService departments,
    PenaltyPolicy penalties,
    IClock clock,
    IOptions<ReportingOptions> options) : PageModel
{
    /// <summary>The "Working days overdue" filter: value of the query string, text, smallest and largest number of days.</summary>
    public static readonly IReadOnlyList<(string Value, string Text, int Min, int? Max)> DayRanges =
    [
        ("1-5", "1–5 working days", 1, 5),
        ("6-10", "6–10 working days", 6, 10),
        ("11+", "More than 10 working days", 11, null),
    ];

    public DashboardReportDto Dashboard { get; private set; } = null!;

    [BindProperty(SupportsGet = true)]
    public Guid? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Days { get; set; }

    public IReadOnlyList<DepartmentDto> Departments { get; private set; } = [];

    public OverdueLoansReportDto Overdue { get; private set; } = null!;

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    /// <summary>The period of the reports below the period form; the current month by default.</summary>
    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    public string? PeriodError { get; private set; }

    public TopBooksAndCategoriesReportDto? Top { get; private set; }

    /// <summary>REP-04 shows department figures, so only the administrator gets it (REP-F5).</summary>
    public ReadingActivityByDepartmentReportDto? ReadingActivity { get; private set; }

    public bool IsAdministrator => User.IsInRole(StaffRoles.LibraryAdministrator);

    /// <summary>REP-09: bar charts of REP-02 to REP-04, for the administrator.</summary>
    public IReadOnlyList<BarChartModel> Charts { get; private set; } = [];

    public int TopBooksLimit => options.Value.TopBooksLimit;

    public int TopCategoriesLimit => options.Value.TopCategoriesLimit;

    public PagedResult<OverdueLoanItemDto> OverduePage =>
        new(Overdue.Items, Overdue.TotalCount, Overdue.Page, Overdue.PageSize);

    // From the penalty policy (Borrowing:Penalties), so the hint follows the settings.
    public int PointsPerLateDay => penalties.CalculateLatePoints(1);

    public int MaxLatePoints => penalties.CalculateLatePoints(int.MaxValue);

    public int CacheMinutes => options.Value.DashboardCacheMinutes;

    public async Task OnGetAsync([FromQuery(Name = PagerModel.PageParameter)] int page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Dashboard = await reports.GetDashboardAsync(ct);   // REP-02

        // REP-01. An unknown department or range from a hand-written URL shows every loan instead of an error.
        Departments = await departments.ListAsync(ct);
        if (DepartmentId is { } id && Departments.All(d => d.Id != id))
            DepartmentId = null;
        var (min, max) = DaysRange();
        Overdue = await reports.GetOverdueLoansAsync(DepartmentId, min, max, new PageRequest(page), ct);

        // REP-03, and REP-04 for the administrator, for the chosen period; a reversed period is shown next to the form instead of the reports.
        SetPeriod();
        try
        {
            Top = await reports.GetTopBooksAndCategoriesAsync(PeriodFrom, PeriodTo, ct);
            if (IsAdministrator)
                ReadingActivity = await reports.GetReadingActivityByDepartmentAsync(PeriodFrom, PeriodTo, ct);
        }
        catch (DomainException ex) when (ex.Error.Kind == ErrorKind.Validation)
        {
            PeriodError = ex.Error.Message;
        }

        if (IsAdministrator)
            Charts = BuildCharts();
    }

    private List<BarChartModel> BuildCharts()
    {
        var d = Dashboard;
        var charts = new List<BarChartModel>
        {
            new("chart-loans", "Loans and staff", $"As of {Date(d.AsOfDate)}",
            [
                new("Active loans", d.ActiveLoans, Num(d.ActiveLoans)),
                new("Overdue loans", d.OverdueLoans, Num(d.OverdueLoans), Warning: true),
                new("Active readers", d.ActiveReaders, Num(d.ActiveReaders)),
                new("Restricted staff", d.RestrictedEmployees, Num(d.RestrictedEmployees), Warning: true),
                new("Suspended staff", d.SuspendedEmployees, Num(d.SuspendedEmployees), Warning: true),
            ]),
        };

        if (Top is { } top)
        {
            string period = $"{Date(top.PeriodStart)} – {Date(top.PeriodEnd)}";
            charts.Add(new("chart-books", "Most borrowed books", $"Loans · {period}",
                top.TopBooks.Select(b => new Bar(b.Title, b.LoanCount, Num(b.LoanCount))).ToList()));
            charts.Add(new("chart-categories", "Top categories", $"Loans · {period}",
                top.TopCategories.Select(c => new Bar(c.Name, c.LoanCount, Num(c.LoanCount))).ToList()));
        }

        if (ReadingActivity is { } reading)
        {
            charts.Add(new("chart-departments", "Loans per 10 staff",
                $"By department · {Date(reading.PeriodStart)} – {Date(reading.PeriodEnd)}",
                reading.Departments.Select(dep => new Bar(dep.Department, dep.LoansPer10Employees,
                    dep.LoansPer10Employees.ToString("0.0", CultureInfo.InvariantCulture))).ToList()));
        }

        return charts;
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"Export CSV" (REP-08): the whole overdue list with its filters, or a period report.</summary>
    public async Task<IActionResult> OnGetCsvAsync(string? report, CancellationToken ct)
    {
        SetPeriod();
        switch (report)
        {
            case "overdue-loans":
                var (min, max) = DaysRange();
                return Csv(report, ReportCsv.OverdueLoans(await reports.GetAllOverdueLoansAsync(DepartmentId, min, max, ct)));
            case "top-books-and-categories":
                return Csv(report, ReportCsv.TopBooksAndCategories(await reports.GetTopBooksAndCategoriesAsync(PeriodFrom, PeriodTo, ct)));
            case "reading-activity-by-department" when IsAdministrator:
                return Csv(report, ReportCsv.ReadingActivityByDepartment(
                    await reports.GetReadingActivityByDepartmentAsync(PeriodFrom, PeriodTo, ct)));
            case "reading-activity-by-department":
                return Forbid();
            default:
                return NotFound();
        }
    }

    public string OverdueCsvHref => ReportPaths.CsvHref(UiNavigation.Reports,
        ("report", "overdue-loans"), ("departmentId", DepartmentId?.ToString()), ("days", Days));

    public string PeriodCsvHref(string report) => ReportPaths.CsvHref(UiNavigation.Reports,
        ("report", report),
        ("from", PeriodFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ("to", PeriodTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    private FileContentResult Csv(string report, byte[] content) => ReportPaths.Csv(this, clock.UtcNow, report, content);

    private (int? Min, int? Max) DaysRange()
    {
        var range = DayRanges.FirstOrDefault(r => r.Value == Days);
        if (range.Value is null)
        {
            Days = null;
            return (null, null);
        }

        return (range.Min, range.Max);
    }

    private void SetPeriod()
    {
        DateOnly today = clock.LocalToday;
        DateOnly monthStart = new(today.Year, today.Month, 1);
        PeriodFrom = From ?? monthStart;
        PeriodTo = To ?? monthStart.AddMonths(1).AddDays(-1);
    }

    public static string Count(int value, string noun) =>
        value.ToString(CultureInfo.InvariantCulture) + " " + noun + (value == 1 ? "" : "s");

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Percent(decimal value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
