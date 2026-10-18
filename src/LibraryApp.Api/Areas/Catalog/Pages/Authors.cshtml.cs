using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages;

[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class AuthorsModel(IAuthorService service) : PageModel
{
    public IReadOnlyList<AuthorDto> Items { get; private set; } = [];
    public AuthorRequest CreateInput { get; private set; } = new(null);
    public AuthorRequest EditInput { get; private set; } = new(null);
    public Guid? EditId { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(AuthorRequest input, CancellationToken ct)
    {
        CreateInput = input;
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await service.CreateAsync(input, ct);
        }
        catch (DomainException ex)
        {
            await LoadAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Author created.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync(Guid id, AuthorRequest input, CancellationToken ct)
    {
        EditId = id;
        EditInput = input;
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await service.UpdateAsync(id, input, ct);
        }
        catch (DomainException ex)
        {
            await LoadAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Author updated.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, bool confirmDelete, CancellationToken ct)
    {
        if (!confirmDelete)
            ModelState.AddModelError(string.Empty, "Tick \"I want to delete this author\" to confirm.");

        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await service.DeleteAsync(id, ct);
        }
        catch (DomainException ex)
        {
            await LoadAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Author deleted.");
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct) => Items = await service.ListAsync(ct);
}
