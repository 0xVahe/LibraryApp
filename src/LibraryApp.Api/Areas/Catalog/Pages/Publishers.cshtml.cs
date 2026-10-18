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
public sealed class PublishersModel(IPublisherService service) : PageModel
{
    public IReadOnlyList<PublisherDto> Items { get; private set; } = [];
    public PublisherRequest CreateInput { get; private set; } = new(null);
    public PublisherRequest EditInput { get; private set; } = new(null);
    public Guid? EditId { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(PublisherRequest input, CancellationToken ct)
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

        TempData.SetAlert(AlertKind.Green, "Publisher created.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync(Guid id, PublisherRequest input, CancellationToken ct)
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

        TempData.SetAlert(AlertKind.Green, "Publisher updated.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
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

        TempData.SetAlert(AlertKind.Green, "Publisher deleted.");
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct) => Items = await service.ListAsync(ct);
}
