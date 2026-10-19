using LibraryApp.Api.Ui.Forms;
using LibraryApp.Application.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

/// <summary>
/// The parts the New book and Edit book pages share (CAT-01): the posted form, the choices of its selects, and the
/// checks of the request. The fields themselves are in <c>_BookFields.cshtml</c>.
/// </summary>
public abstract class BookFormPageModel(ICategoryService categories, IPublisherService publishers) : PageModel
{
    [BindProperty]
    public BookFormInput Input { get; set; } = new();

    public IReadOnlyList<CatalogLanguage> Languages => CatalogLanguages.All;

    public IReadOnlyList<PublisherDto> Publishers { get; private set; } = [];

    /// <summary>"Parent / Child" choices, each top-level category followed by its subcategories.</summary>
    public IReadOnlyList<CategoryOption> Categories { get; private set; } = [];

    /// <summary>The domain error of the last post (a duplicate ISBN, a book with copies on loan), shown above the form.</summary>
    public string? FormError =>
        ModelState.TryGetValue(string.Empty, out var entry) && entry.Errors.Count > 0
            ? string.Join(" ", entry.Errors.Select(error => error.ErrorMessage))
            : null;

    /// <summary>"true" for a field with an error (the input gets <c>aria-invalid</c>), otherwise null (no attribute).</summary>
    public string? AriaInvalid(string field) =>
        ModelState.TryGetValue($"{nameof(Input)}.{field}", out var entry) && entry.Errors.Count > 0 ? "true" : null;

    protected async Task LoadChoicesAsync(CancellationToken ct)
    {
        Categories = new CategoryTree(await categories.ListAsync(ct)).Options;
        Publishers = (await publishers.ListAsync(ct))
            .OrderBy(publisher => publisher.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Runs the request's validator, so the page applies exactly the rules of the API; errors go next to the fields
    /// (<c>Input.Title</c>, <c>Input.Isbn</c>, ...).
    /// </summary>
    protected Task ValidateAsync(object request, CancellationToken ct) =>
        UiValidationFilter.ValidateAsync(HttpContext.RequestServices, request, nameof(Input), ModelState, ct);
}
