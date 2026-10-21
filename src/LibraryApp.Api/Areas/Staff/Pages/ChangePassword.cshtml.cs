using LibraryApp.Api.Auth;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Auth;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>
/// MEM-02 change password page: the same change as POST /api/auth/change-password (<see cref="IPasswordChangeService"/>:
/// current password, password rules, lockout). Every signed-in user may open it; first-login accounts are sent here by
/// <c>InitialPasswordChangeMiddleware</c>. On success the user is signed out and signs in again with the new password
/// (the new security stamp would end the cookie session anyway).
/// </summary>
[EnableRateLimiting(LoginRateLimiting.PolicyName)] // the policy counts POSTs only
public sealed class ChangePasswordModel(IPasswordChangeService passwords, IUiSignIn signIn) : PageModel
{
    public const string PasswordChangedParameter = "passwordChanged";

    [BindProperty] public ChangePasswordRequest Input { get; set; } = new(null, null);

    [BindProperty] public string? RepeatPassword { get; set; }

    [BindProperty(SupportsGet = true, Name = LoginRateLimiting.TooManyAttemptsParameter)]
    public bool TooManyAttempts { get; set; }

    public AlertModel? Alert { get; private set; }

    public bool FirstLogin => User.HasClaim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString);

    public void OnGet()
    {
        if (TooManyAttempts)
            Alert = new AlertModel(AlertKind.Red, "Too many attempts",
                "Too many password change attempts from this computer. Try again later.");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!string.Equals(RepeatPassword, Input.NewPassword, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(RepeatPassword), "The passwords do not match.");
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await passwords.ChangeAsync(Input, ct);
        }
        catch (DomainException ex) // wrong current password, locked account or a password the rules reject
        {
            Alert = new AlertModel(AlertKind.Red, ex.Error.Message);
            return Page();
        }

        await signIn.SignOutAsync(HttpContext);
        return LocalRedirect($"{UiPaths.Login}?{PasswordChangedParameter}=true");
    }

    /// <summary>The first validation message of a form field, or null.</summary>
    public string? FieldError(string key) =>
        ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? entry.Errors[0].ErrorMessage : null;
}