using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Borrowing.Pages;

/// <summary>
/// Declares the copy of an open loan lost (BOR-09) from the "Declare lost" button of a loan row in
/// <c>StaffBorrowingSummary</c> on Group 2's Staff member page, then goes back to that page (Post/Redirect/Get) with an
/// amber or red <c>_Alert</c>. Library staff only: the service leaves the role to its caller, as the API does.
/// The form lives on another group's page, so a refusal (already returned or lost, not past due) is a red alert there.
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class DeclareLostModel(ILoanLostService lost) : PageModel
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
            DeclareLostResponse result = await lost.DeclareLostAsync(LoanId, ct);
            TempData.SetAlert(AlertKind.Amber, "Declared lost", DeskModel.LostText(result));
        }
        catch (DomainException ex) when (ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule)
        {
            TempData.SetAlert(AlertKind.Red, "The copy was not declared lost.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : UiNavigation.Staff);
    }
}
