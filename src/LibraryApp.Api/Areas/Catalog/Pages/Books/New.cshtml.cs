using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

/// <summary>
/// New book (CAT-01, BR-CAT-5): the book form plus its authors, which a new book needs. Copies are added afterwards
/// on the Edit book page, where the librarian lands after saving.
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class NewModel(
    IBookService books,
    IAuthorService authors,
    ICategoryService categories,
    IPublisherService publishers) : BookFormPageModel(categories, publishers)
{
    public IReadOnlyList<AuthorDto> Authors { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Input = new BookFormInput { Language = "en" };
        await LoadAllChoicesAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var request = Input.ToCreateRequest();
        await ValidateAsync(request, ct);
        if (!ModelState.IsValid)
        {
            await LoadAllChoicesAsync(ct);
            return Page();
        }

        Guid id;
        try
        {
            id = await books.CreateAsync(request, ct);
        }
        catch (DomainException ex)
        {
            await LoadAllChoicesAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Book added.", "Add its copies below.");
        return LocalRedirect($"/catalog/books/{id}/edit");
    }

    /// <summary>The selects of the book form plus the authors list.</summary>
    private async Task LoadAllChoicesAsync(CancellationToken ct)
    {
        await LoadChoicesAsync(ct);
        Authors = (await authors.ListAsync(ct))
            .OrderBy(author => author.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
