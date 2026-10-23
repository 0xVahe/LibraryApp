using System.Globalization;
using LibraryApp.Api.Controllers.Reporting;
using LibraryApp.Api.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Reporting.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>REP-05: copies by status and condition, for the whole library or one category with its subcategories.</summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class InventoryModel(IReportingService reports, ICategoryService categories, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? CategoryId { get; set; }

    /// <summary>Top-level categories by name, each followed by its subcategories.</summary>
    public IReadOnlyList<(Guid Id, string Text)> Categories { get; private set; } = [];

    public InventoryStatusReportDto Report { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        await LoadCategoriesAsync(ct);
        Report = await reports.GetInventoryStatusAsync(CategoryId, ct);
    }

    /// <summary>"Export CSV" of the category shown (REP-08).</summary>
    public async Task<IActionResult> OnGetCsvAsync(CancellationToken ct)
    {
        await LoadCategoriesAsync(ct);
        return ReportPaths.Csv(this, clock.UtcNow, "inventory-status",
            ReportCsv.InventoryStatus(await reports.GetInventoryStatusAsync(CategoryId, ct)));
    }

    public string CsvHref => ReportPaths.CsvHref(ReportPaths.Inventory, ("categoryId", CategoryId?.ToString()));

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        IReadOnlyList<CategoryDto> all = await categories.ListAsync(ct);
        Categories = all
            .Where(category => category.ParentCategoryId is null)
            .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(parent => all
                .Where(child => child.ParentCategoryId == parent.Id)
                .OrderBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(child => (child.Id, $"{parent.Name} · {child.Name}"))
                .Prepend((parent.Id, parent.Name)))
            .ToList();

        // An unknown category from a hand-written URL shows the whole library instead of an error.
        if (CategoryId is { } id && Categories.All(category => category.Id != id))
            CategoryId = null;
    }

    public string Generated(DateTime utc) => clock.ToLocal(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
