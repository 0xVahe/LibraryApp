using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

/// <summary>
/// The authors picker of the Edit book page (CAT-02): replaces the book's authors, at least one (BR-CAT-5). Like the
/// copies panel, it goes back to the Edit book page (Post/Redirect/Get) with a green or red alert.
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class AuthorsModel(IAuthorService authors) : PageModel
{
    private const string NotSaved = "The authors were not saved.";

    public IActionResult OnGet(Guid bookId) => LocalRedirect(EditPath(bookId));

    /// <param name="bookId">The book, from the address.</param>
    /// <param name="authorIds">The selected options of the picker; none selected posts an empty list.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<IActionResult> OnPostAsync(Guid bookId, List<Guid> authorIds, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Refused(bookId, "Choose the authors from the list.");

        try
        {
            await authors.SetBookAuthorsAsync(bookId, new BookAuthorsRequest(authorIds), ct);
        }
        catch (DomainException ex) when (ex.Error == CatalogErrors.AuthorNotFound)
        {
            // An author deleted while the page was open: a form error, not the Not found page of the book.
            return Refused(bookId, "One of the chosen authors no longer exists. Choose the authors again.");
        }
        catch (DomainException ex) when (ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule)
        {
            return Refused(bookId, ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;   // an unknown or deleted book: the Not found page
        }

        TempData.SetAlert(AlertKind.Green, "Authors saved.");
        return LocalRedirect(EditPath(bookId));
    }

    /// <summary>The Edit book page, where the authors picker is.</summary>
    private static string EditPath(Guid bookId) => $"/catalog/books/{bookId}/edit";

    private IActionResult Refused(Guid bookId, string message)
    {
        TempData.SetAlert(AlertKind.Red, NotSaved, message);
        return LocalRedirect(EditPath(bookId));
    }
}
