using LibraryApp.Api.Controllers.Reporting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>The report pages opened from "More reports" on the Reports page.</summary>
public static class ReportPaths
{
    public const string Inventory = "/reports/inventory";
    public const string Penalties = "/reports/penalties";
    public const string PurchaseCandidates = "/reports/purchase-candidates";

    /// <summary>"Export CSV" of a page: its <c>csv</c> handler with the values of the report shown (REP-08).</summary>
    public static string CsvHref(string path, params (string Name, string? Value)[] values)
    {
        var query = new QueryBuilder { { "handler", "csv" } };
        foreach (var (name, value) in values)
        {
            if (value is not null)
                query.Add(name, value);
        }

        return path + query;
    }

    /// <summary>The CSV file of a report, the same as the API export (the API itself takes a token, not the page cookie).</summary>
    public static FileContentResult Csv(PageModel page, DateTime utcNow, string report, byte[] content)
    {
        page.Response.Headers.CacheControl = "no-store";
        return page.File(content, ReportCsv.ContentType, ReportCsv.FileName(report, utcNow));
    }
}
