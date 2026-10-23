using System.Globalization;
using LibraryApp.Api.Controllers.Reporting;
using LibraryApp.Api.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>REP-06: penalty points by department; department figures are for the administrator only (REP-F5).</summary>
[Authorize(Policy = UiPolicies.Administrator)]
public sealed class PenaltiesModel(IReportingService reports, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public DateOnly PeriodFrom { get; private set; }

    public DateOnly PeriodTo { get; private set; }

    public string? PeriodError { get; private set; }

    public PenaltyReportDto? Report { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        SetPeriod();
        try
        {
            Report = await reports.GetPenaltiesByDepartmentAsync(PeriodFrom, PeriodTo, ct);
        }
        catch (DomainException ex) when (ex.Error.Kind == ErrorKind.Validation)
        {
            PeriodError = ex.Error.Message;
        }
    }

    /// <summary>"Export CSV" of the period shown (REP-08).</summary>
    public async Task<IActionResult> OnGetCsvAsync(CancellationToken ct)
    {
        SetPeriod();
        return ReportPaths.Csv(this, clock.UtcNow, "penalties",
            ReportCsv.Penalties(await reports.GetPenaltiesByDepartmentAsync(PeriodFrom, PeriodTo, ct)));
    }

    public string CsvHref => ReportPaths.CsvHref(ReportPaths.Penalties,
        ("from", PeriodFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        ("to", PeriodTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    private void SetPeriod()
    {
        DateOnly today = clock.LocalToday;
        DateOnly monthStart = new(today.Year, today.Month, 1);
        PeriodFrom = From ?? monthStart;
        PeriodTo = To ?? monthStart.AddMonths(1).AddDays(-1);
    }

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
