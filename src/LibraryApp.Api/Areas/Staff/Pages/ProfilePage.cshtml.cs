using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>
/// My profile (S2-01, MEM-08): the caller's own details. Staff members change their first and last name here, like
/// PUT /api/staff-members/me; library staff change theirs on their Staff member page. The password card posts to the
/// Change password page (MEM-02).
/// </summary>
public sealed class ProfileModel(IMyLibraryService myLibrary) : PageModel
{
    [BindProperty]
    public UpdateMyProfileRequest Input { get; set; } = new("", "");

    public MyProfileDto Profile { get; private set; } = null!;

    /// <summary>Same rule as the API: only the StaffMember role edits its own name.</summary>
    public bool CanEditName => User.IsInRole(StaffRoles.StaffMember);

    public string? Invalid(string field) =>
        ModelState.TryGetValue($"Input.{field}", out var entry) && entry.Errors.Count > 0 ? "true" : null;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        Input = new(Profile.FirstName, Profile.LastName);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!CanEditName)
            return Forbid();

        await LoadAsync(ct);
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await myLibrary.UpdateProfileAsync(Input, ct);
        }
        catch (DomainException exception)
        {
            return this.DomainError(exception);
        }

        TempData.SetAlert(AlertKind.Green, "Profile updated.");
        return LocalRedirect(UiNavigation.Profile);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Profile = (await myLibrary.GetAsync(ct)).Profile;
    }
}