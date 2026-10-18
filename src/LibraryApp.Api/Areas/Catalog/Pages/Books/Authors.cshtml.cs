using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class AuthorsModel(
    IAuthorService authors,
    IBookService books,
    IBookDetailsService details,
    ICategoryService categories,
    IPublisherService publishers) : PageModel
{
    [BindProperty]
    public Guid BookId { get; set; }

    [BindProperty]
    public List<Guid> AuthorIds { get; set; } = [];

    public EditModel Edit { get; private set; } = null!;

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadEditAsync(ct);
            return Page();
        }

        try
        {
            await authors.SetBookAuthorsAsync(BookId, new BookAuthorsRequest(AuthorIds), ct);
        }
        catch (DomainException ex)
        {
            var result = this.DomainError(ex);
            if (result is PageResult)
                await LoadEditAsync(ct);
            return result;
        }

        TempData.SetAlert(AlertKind.Green, "Book authors saved.");
        return LocalRedirect($"/catalog/books/{BookId}/edit");
    }

    private async Task LoadEditAsync(CancellationToken ct)
    {
        Edit = new EditModel(books, details, categories, publishers, authors) { PageContext = PageContext };
        await Edit.OnGetAsync(BookId, ct);
        Edit.AuthorIds = AuthorIds;
    }
}
