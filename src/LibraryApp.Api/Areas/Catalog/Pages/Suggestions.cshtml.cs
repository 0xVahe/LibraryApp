using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages;

/// <summary>
/// Suggest a book (CAT-10), for every signed-in role: the form, and the list of my own suggestions with their status.
/// The Library Administrator reviews them on <see cref="SuggestionReviewModel"/>.
/// </summary>
[Authorize(Policy = UiPolicies.SignedIn)]
public sealed class SuggestionsModel(IBookSuggestionService suggestions, IClock clock) : PageModel
{
    public const string Path = "/catalog/suggestions";

    /// <summary>The posted form; checked by <c>CreateBookSuggestionRequestValidator</c> (errors under <c>Input.Title</c>, ...).</summary>
    [BindProperty]
    public CreateBookSuggestionRequest Input { get; set; } = new(null, null, null, null);

    public PagedResult<BookSuggestionDto> Mine { get; private set; } = PagedResult<BookSuggestionDto>.Empty(new PageRequest());

    public bool CanReview => User.IsInRole(StaffRoles.LibraryAdministrator);

    /// <summary>A domain error of the last post, shown above the form.</summary>
    public string? FormError =>
        ModelState.TryGetValue(string.Empty, out var entry) && entry.Errors.Count > 0
            ? string.Join(" ", entry.Errors.Select(error => error.ErrorMessage))
            : null;

    /// <summary>"true" for a field with an error (the input gets <c>aria-invalid</c>), otherwise null (no attribute).</summary>
    public string? AriaInvalid(string field) =>
        ModelState.TryGetValue($"{nameof(Input)}.{field}", out var entry) && entry.Errors.Count > 0 ? "true" : null;

    public string LocalDate(DateTime utc) => clock.ToLocalDate(utc).ToString("yyyy-MM-dd");

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await suggestions.CreateAsync(Input, ct);
        }
        catch (DomainException ex)
        {
            await LoadAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Thank you, your suggestion was sent.", "The Library Administrator reviews it; its status is in the list below.");
        return Redirect(Path);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        int page = int.TryParse(Request.Query[PagerModel.PageParameter], out var requested) ? requested : 1;
        Mine = await suggestions.ListMineAsync(new PageRequest(page), ct);
    }
}
