using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Catalog.Components;

/// <summary>
/// The copies panel of the Edit book page (CAT-04): every copy, Lost and Withdrawn included, with its status, shelf
/// and condition, the allowed status changes, and the form that adds a copy. It loads its own data, so the Edit page
/// only places it: <c>&lt;vc:copies-panel book-id="@Model.BookId" /&gt;</c>. Its forms post to the Copies page
/// (<see cref="Pages.Books.CopiesModel"/>), which comes back to the Edit page with an alert.
/// </summary>
public sealed class CopiesPanelViewComponent(IBookCopyService copies) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid bookId)
    {
        var bookCopies = await copies.GetCopiesAsync(bookId, HttpContext.RequestAborted);
        // Copies still in the collection first, then Lost and Withdrawn; by barcode within each group.
        var ordered = bookCopies
            .OrderBy(copy => CopyStatusActions.CanEditDetails(copy.Status) ? 0 : 1)
            .ThenBy(copy => copy.Barcode, StringComparer.Ordinal)
            .ToList();
        return View("~/Areas/Catalog/Components/CopiesPanel/Default.cshtml", new CopiesPanelModel(bookId, ordered));
    }
}

public sealed record CopiesPanelModel(Guid BookId, IReadOnlyList<BookCopyDto> Copies);
