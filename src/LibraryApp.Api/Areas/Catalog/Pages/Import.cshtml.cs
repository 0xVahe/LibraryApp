using LibraryApp.Api.Catalog;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Areas.Catalog.Pages;

/// <summary>
/// Import books (CAT-09): the librarian uploads a CSV file. Every row is checked first; either all books are added,
/// or none are and the page lists every row error (row, field, message).
/// </summary>
/// <remarks>
/// The request limit is a host cap above the <c>Catalog:Import:MaxFileBytes</c> rule (five times the file limit,
/// <see cref="CatalogImportRequestLimitsFilter"/>): a file up to the cap reaches the import service, which refuses
/// anything over the limit with a clear row error instead of an error page.
/// </remarks>
[Authorize(Policy = UiPolicies.LibraryStaff)]
[CatalogImportRequestLimits(forPage: true)]
public sealed class ImportModel(IBookImportService importService, IOptions<CatalogImportOptions> importOptions) : PageModel
{
    /// <summary>The <c>Catalog:Import</c> limits, shown next to the file field.</summary>
    public CatalogImportOptions Limits => importOptions.Value;

    /// <summary>The sample file in wwwroot.</summary>
    public const string SamplePath = "/samples/book-import.csv";

    /// <summary>The uploaded file (form field "Csv").</summary>
    [BindProperty]
    public IFormFile? Csv { get; set; }

    /// <summary>The row errors of a refused file; empty when nothing was refused.</summary>
    public IReadOnlyList<BookImportError> RowErrors { get; private set; } = [];

    /// <summary>A message for the whole form (no file, or another rule broken), or null.</summary>
    public string? FormError =>
        ModelState.TryGetValue(string.Empty, out var entry) && entry.Errors.Count > 0
            ? string.Join(" ", entry.Errors.Select(error => error.ErrorMessage))
            : null;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Csv is not { Length: > 0 })
        {
            ModelState.AddModelError(string.Empty, "Choose a CSV file to import.");
            return Page();
        }

        BookImportResult result;
        try
        {
            await using var stream = Csv.OpenReadStream();
            result = await importService.ImportAsync(stream, Csv.Length, ct);
        }
        catch (DomainException ex) when (ex.Error == CatalogErrors.ImportInvalid)
        {
            RowErrors = ex.Extensions.TryGetValue("rows", out var rows) && rows is IReadOnlyList<BookImportError> list
                ? list
                : [];
            if (RowErrors.Count == 0)
                ModelState.AddModelError(string.Empty, ex.Error.Message);
            return Page();
        }
        catch (DomainException ex)
        {
            return this.DomainError(ex);
        }

        string books = result.CreatedCount == 1 ? "1 book" : $"{result.CreatedCount} books";
        TempData.SetAlert(AlertKind.Green, $"Imported {books}.", "They have no copies yet: add the copies on each book's Edit page.");
        return RedirectToPage();
    }
}
