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
/// Renews a loan (BOR-04, BR-BOR-1) from the Renew button of <c>MyLoans</c>, then goes back to the page of the button
/// (Post/Redirect/Get) with a green or red <c>_Alert</c>. A Staff Member renews only their own loans (NFR-03, checked by
/// the service). Not found and Forbidden get their pages, as everywhere (<see cref="DomainErrors"/>).
/// </summary>
public sealed class RenewLoanModel(ILoanRenewalService renewals, IClock clock) : PageModel
{
    [BindProperty]
    public Guid LoanId { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (LoanId == Guid.Empty)
            return NotFound();

        try
        {
            LoanDto loan = await renewals.RenewAsync(LoanId, ct);
            TempData.SetAlert(AlertKind.Green, "Loan renewed.", $"The new due date is {Texts.Date(clock.ToLocalDate(loan.DueAtUtc))}.");
        }
        catch (DomainException ex) when (ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule)
        {
            TempData.SetAlert(AlertKind.Red, "The loan was not renewed.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(BackTo(Url, ReturnUrl));
    }

    /// <summary>The page of the button (a local URL only), My library otherwise: the blocks live there.</summary>
    public static string BackTo(IUrlHelper url, string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && url.IsLocalUrl(returnUrl) ? returnUrl : UiNavigation.MyLibrary;
}
