using System.Globalization;
using LibraryApp.Api.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>
/// The reports of the library (REP-F1): Librarian and Library Administrator only. Read only; every report states its
/// period or date and when it was generated (REP-F3).
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class IndexModel(IReportingService reports, IClock clock, IOptions<ReportingOptions> options) : PageModel
{
    public DashboardReportDto Dashboard { get; private set; } = null!;

    public int CacheMinutes => options.Value.DashboardCacheMinutes;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Dashboard = await reports.GetDashboardAsync(ct);   // REP-02
    }

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Percent(decimal value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
