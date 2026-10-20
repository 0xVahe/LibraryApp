using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Borrowing.Pages;

/// <summary>
/// Cancels a Waiting or ReadyForPickup reservation (BOR-12) from the Cancel button of <c>MyReservations</c>, then goes
/// back to the page of the button (Post/Redirect/Get) with a green or red <c>_Alert</c>. A Staff Member cancels only
/// their own (NFR-03, checked by the service); a held copy goes to the next member in the queue or back to the shelf.
/// </summary>
public sealed class CancelReservationModel(IReservationService reservations) : PageModel
{
    [BindProperty]
    public Guid ReservationId { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ReservationId == Guid.Empty)
            return NotFound();

        try
        {
            await reservations.CancelAsync(ReservationId, ct);
            TempData.SetAlert(AlertKind.Green, "Reservation cancelled.", "Your place in the queue is released.");
        }
        catch (DomainException ex) when (ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule)
        {
            TempData.SetAlert(AlertKind.Red, "The reservation was not cancelled.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(RenewLoanModel.BackTo(Url, ReturnUrl));
    }
}
