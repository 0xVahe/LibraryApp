using System.Globalization;
using LibraryApp.Api.Controllers.Reporting;
using LibraryApp.Api.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>REP-07: the books with the longest reservation queues. Administrator only, like the report.</summary>
[Authorize(Policy = UiPolicies.Administrator)]
public sealed class PurchaseCandidatesModel(IReportingService reports, IClock clock) : PageModel
{
    public PurchaseCandidatesReportDto Report { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Report = await reports.GetPurchaseCandidatesAsync(ct);
    }

    /// <summary>"Export CSV" (REP-08).</summary>
    public async Task<IActionResult> OnGetCsvAsync(CancellationToken ct) =>
        ReportPaths.Csv(this, clock.UtcNow, "purchase-candidates", ReportCsv.PurchaseCandidates(await reports.GetPurchaseCandidatesAsync(ct)));

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
