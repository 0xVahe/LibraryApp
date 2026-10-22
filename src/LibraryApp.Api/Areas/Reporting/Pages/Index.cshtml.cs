using System.Globalization;
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
        var range = DayRanges.FirstOrDefault(r => r.Value == Days);
        if (range.Value is null)
            Days = null;
        Overdue = await reports.GetOverdueLoansAsync(DepartmentId, range.Value is null ? null : range.Min, range.Max, new PageRequest(page), ct);
    }

    public static string Count(int value, string noun) =>
        value.ToString(CultureInfo.InvariantCulture) + " " + noun + (value == 1 ? "" : "s");

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Percent(decimal value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
