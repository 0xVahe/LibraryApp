using System.Text.Json;
using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LibraryApp.Api.Areas.Borrowing.Pages;

/// <summary>
/// Waives one active penalty record with a mandatory reason (BOR-08, G3-23) from its form in the "Standing" card of
/// <c>StaffBorrowingSummary</c> on Group 2's Staff member page, then goes back to that page (Post/Redirect/Get). Library
/// staff only: the G3-08 service leaves the role to its caller, as the API does.
/// <para>
/// The form lives on another group's page, so a reason error travels back in TempData (<see cref="WaiveFormError"/>)
/// and is shown beside the reason field of that record's form, which keeps what was typed. Any other refusal (the record
/// is no longer active) is a red alert; success is a green one.
/// </para>
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class WaivePenaltyModel(IPenaltyAdministrationService penalties) : PageModel
{
    [BindProperty]
    public Guid PenaltyId { get; set; }

    /// <summary>Validated by <c>WaivePenaltyRequestValidator</c> through the shell's page filter (<c>Input.Reason</c>).</summary>
    [BindProperty]
    public WaivePenaltyRequest Input { get; set; } = new("");

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (PenaltyId == Guid.Empty)
            return NotFound();

        if (!ModelState.IsValid)
        {
            // The validator's message comes after the model binder's generic one; it is the one the form shows.
            string message = ModelState.TryGetValue("Input.Reason", out var entry) && entry.Errors.Count > 0
                ? entry.Errors[^1].ErrorMessage
                : BorrowingErrors.WaiverReasonRequired.Message;
            WaiveFormError.Set(TempData, new WaiveFormError(PenaltyId, Input?.Reason, message));
            return LocalRedirect(Back);
        }

        try
        {
            PenaltyDto waived = await penalties.WaiveAsync(PenaltyId, Input, ct);
            TempData.SetAlert(AlertKind.Green, "Penalty waived.",
                $"{Texts.Count(waived.Points, "point")} waived. The staff member's standing is recalculated.");
        }
        catch (DomainException ex) when (ex.Error == BorrowingErrors.WaiverReasonRequired)
        {
            WaiveFormError.Set(TempData, new WaiveFormError(PenaltyId, Input.Reason, ex.Error.Message));
        }
        catch (DomainException ex) when (ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule)
        {
            TempData.SetAlert(AlertKind.Red, "The penalty was not waived.", ex.Error.Message);
        }
        catch (DomainException ex) when (DomainErrors.ResultFor(ex) is { } result)
        {
            return result;
        }

        return LocalRedirect(Back);
    }

    /// <summary>The Staff member page of the form (a local URL only), the staff list otherwise.</summary>
    private string Back => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : UiNavigation.Staff;
}

/// <summary>A refused reason of one waiver form, shown once beside that form's field after the redirect.</summary>
public sealed record WaiveFormError(Guid PenaltyId, string? Reason, string Message)
{
    private const string Key = "borrowing.waive-error";

    public static void Set(ITempDataDictionary tempData, WaiveFormError error) => tempData[Key] = JsonSerializer.Serialize(error);

    public static WaiveFormError? Take(ITempDataDictionary tempData) =>
        tempData[Key] is string json ? JsonSerializer.Deserialize<WaiveFormError>(json) : null;
}
