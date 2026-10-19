using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Borrowing.Pages;

/// <summary>
/// Places the signed-in member in the queue of a book (BOR-07; BR-BOR-3 refuses while a copy is Available) from the
/// ReservePanel on the Book page, then goes back to that page (Post/Redirect/Get) with a green or red <c>_Alert</c>.
/// The form lives on another group's page, so a refused reservation is shown there as a red alert, not on this page.
/// Not found and Forbidden get their pages, as everywhere (<see cref="DomainErrors"/>).
/// </summary>
public sealed class ReserveModel(IReservationService reservations, ICurrentUser currentUser) : PageModel
{
    [BindProperty]
    public Guid BookId { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (BookId == Guid.Empty || currentUser.StaffMemberId is not { } member)
            return NotFound();

        try
        {
            ReservationDto reservation = await reservations.CreateAsync(new CreateReservationRequest(BookId, member), ct);
            TempData.SetAlert(AlertKind.Green, "Book reserved.",
                $"You are {ReservePanelModel.Ordinal(reservation.QueuePosition)} in the queue. You get an email when your copy is ready.");
        }
        catch (DomainException ex) when (ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule)
        {
            TempData.SetAlert(AlertKind.Red, "The book was not reserved.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(BookPath(BookId));
    }

    /// <summary>Group 1's Book page (UI guide Part 2).</summary>
    public static string BookPath(Guid bookId) => $"{UiNavigation.Catalog}/books/{bookId}";
}
