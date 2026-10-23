using System.Globalization;
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

        DateOnly today = clock.LocalToday;
        DateOnly monthStart = new(today.Year, today.Month, 1);
        PeriodFrom = From ?? monthStart;
        PeriodTo = To ?? monthStart.AddMonths(1).AddDays(-1);
        try
        {
            Report = await reports.GetPenaltiesByDepartmentAsync(PeriodFrom, PeriodTo, ct);
        }
        catch (DomainException ex) when (ex.Error.Kind == ErrorKind.Validation)
        {
            PeriodError = ex.Error.Message;
        }
    }

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
