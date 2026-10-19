using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

/// <summary>
/// The actions of the copies panel (CAT-04): add a copy, change its shelf and condition, change its status. Each goes
/// back to the Edit book page (Post/Redirect/Get) with a green or red alert, because the forms live on that page.
/// A new or repaired copy can come back Reserved when a reader is waiting; the alert says so.
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class CopiesModel(IBookCopyService copies) : PageModel
{
    public IActionResult OnGet(Guid bookId) => LocalRedirect(EditPath(bookId));

    public async Task<IActionResult> OnPostAddAsync(Guid bookId, AddCopyRequest input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Refused(bookId, "The copy was not added.", FormErrors());

        try
        {
            var copy = await copies.AddCopyAsync(bookId, input, ct);
            if (copy.Status == BookCopyStatus.Reserved)
                TempData.SetAlert(AlertKind.Green, $"Copy {copy.Barcode} added.",
                    "A reader was waiting for this book, so the copy is now held for them (Reserved).");
            else
                TempData.SetAlert(AlertKind.Green, $"Copy {copy.Barcode} added.");
        }
        catch (DomainException ex) when (IsFormError(ex))
        {
            return Refused(bookId, "The copy was not added.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(EditPath(bookId));
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid bookId, Guid copyId, UpdateCopyRequest input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Refused(bookId, "The copy was not changed.", FormErrors());

        try
        {
            var copy = await copies.UpdateCopyAsync(bookId, copyId, input, ct);
            TempData.SetAlert(AlertKind.Green, $"Copy {copy.Barcode} saved.");
        }
        catch (DomainException ex) when (IsFormError(ex))
        {
            return Refused(bookId, "The copy was not changed.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(EditPath(bookId));
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid bookId, Guid copyId, BookCopyStatus status, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Refused(bookId, "The status was not changed.", FormErrors());

        try
        {
            var copy = await copies.ChangeStatusAsync(bookId, copyId, new ChangeCopyStatusRequest(status), ct);
            TempData.SetAlert(AlertKind.Green, StatusMessage(copy, status));
        }
        catch (DomainException ex) when (IsFormError(ex))
        {
            return Refused(bookId, "The status was not changed.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(EditPath(bookId));
    }

    /// <summary>The Edit book page, where the copies panel is.</summary>
    public static string EditPath(Guid bookId) => $"/catalog/books/{bookId}/edit";

    /// <summary>The status the service returned: a repaired copy can come back Reserved for a waiting reader.</summary>
    private static string StatusMessage(BookCopyDto copy, BookCopyStatus requested) =>
        requested == BookCopyStatus.Available && copy.Status == BookCopyStatus.Reserved
            ? $"Copy {copy.Barcode} is back and held for a waiting reader (Reserved)."
            : $"Copy {copy.Barcode} is now {StatusBadge.Label(copy.Status).ToLowerInvariant()}.";

    private static bool IsFormError(DomainException ex) =>
        ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule;

    private IActionResult Refused(Guid bookId, string title, string message)
    {
        TempData.SetAlert(AlertKind.Red, title, message);
        return LocalRedirect(EditPath(bookId));
    }

    private string FormErrors() =>
        string.Join(" ", ModelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage).Distinct());
}
