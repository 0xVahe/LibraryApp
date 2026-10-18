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
/// Edit book (CAT-01): change the details, or delete the book (soft delete; BR-CAT-3 refuses it while a copy is on
/// loan or reserved). The copies panel (CAT-04) and the authors picker (CAT-02) are added to this page by their owners.
/// An unknown or deleted book shows the Not found page.
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class EditModel(
    IBookService books,
    IBookDetailsService details,
    ICategoryService categories,
    IPublisherService publishers,
    IAuthorService authors) : BookFormPageModel(categories, publishers)
{
    public Guid BookId { get; private set; }

    public string BookTitle { get; private set; } = "";

    public IReadOnlyList<AuthorDto> Authors { get; private set; } = [];

    public IReadOnlyList<Guid> AuthorIds { get; set; } = [];

    public async Task OnGetAsync(Guid id, CancellationToken ct)
    {
        var book = await LoadBookAsync(id, ct);
        Input = BookFormInput.From(book);
        await LoadChoicesAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        await LoadBookAsync(id, ct);

        var request = Input.ToUpdateRequest();
        await ValidateAsync(request, ct);
        if (!ModelState.IsValid)
        {
            await LoadChoicesAsync(ct);
            return Page();
        }

        try
        {
            await books.UpdateAsync(id, request, ct);
        }
        catch (DomainException ex)
        {
            await LoadChoicesAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, "Book saved.");
        return LocalRedirect($"/catalog/books/{id}/edit");
    }

    /// <param name="id">The book.</param>
    /// <param name="confirmDelete">The "I want to delete this book" box: nothing is deleted without it.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id, bool confirmDelete, CancellationToken ct)
    {
        var book = await LoadBookAsync(id, ct);

        // The delete form posts no book fields: show the saved details again, not an empty form.
        ModelState.Clear();
        Input = BookFormInput.From(book);

        if (!confirmDelete)
        {
            ModelState.AddModelError(string.Empty, "Tick \"I want to delete this book\" to confirm.");
            await LoadChoicesAsync(ct);
            return Page();
        }

        try
        {
            await books.DeleteAsync(id, ct);
        }
        catch (DomainException ex)
        {
            await LoadChoicesAsync(ct);
            return this.DomainError(ex);
        }

        TempData.SetAlert(AlertKind.Green, $"\u201c{book.Title}\u201d was deleted.");
        return LocalRedirect("/catalog");
    }

    private async Task<BookDetailsDto> LoadBookAsync(Guid id, CancellationToken ct)
    {
        var book = await details.GetBookDetailsAsync(id, ct);   // unknown or deleted → Not found page
        BookId = book.Id;
        BookTitle = book.Title;
        Authors = (await authors.ListAsync(ct))
            .OrderBy(author => author.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AuthorIds = await authors.GetBookAuthorIdsAsync(id, ct);
        return book;
    }
}
