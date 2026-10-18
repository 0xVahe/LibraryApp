using LibraryApp.Api.Auth;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Auth;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>
/// MEM-02 sign-in page: the same login as POST /api/auth/login (<see cref="IAuthService"/>: corporate email, lockout,
/// deactivated accounts), then the cookie sign-in of the shell. POSTs share the API's per-IP limit; a rejected POST
/// comes back here with <see cref="TooManyAttemptsParameter"/> (see <c>LoginRateLimiting.OnRejected</c>).
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(LoginRateLimiting.PolicyName)]   // the policy counts POSTs only
public sealed class LoginModel(IAuthService auth, IUiSignIn signIn, IOptions<IdentityOptions> identityOptions) : PageModel
{
    public const string TooManyAttemptsParameter = "tooManyAttempts";
    private const string ReturnUrlParameter = "returnUrl";

    [BindProperty]
    public LoginRequest Input { get; set; } = new(null, null);

    [BindProperty(SupportsGet = true, Name = ReturnUrlParameter)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = TooManyAttemptsParameter)]
    public bool TooManyAttempts { get; set; }

    public AlertModel? Alert { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl);

        if (TooManyAttempts)
            Alert = new AlertModel(AlertKind.Red, "Too many attempts", "Too many sign-in attempts from this computer. Try again later.");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        LoginResponse? login;
        try
        {
            login = await auth.LoginAsync(Input, ct);
        }
        catch (DomainException ex)   // deactivated account (403) or not corporate: shown on the form, not as the Forbidden page
        {
            Alert = new AlertModel(AlertKind.Red, ex.Error.Message);
            return Page();
        }

        // Unknown email, wrong password and a locked account get the same answer, so accounts cannot be guessed.
        if (login is null)
        {
            var lockout = identityOptions.Value.Lockout;
            Alert = new AlertModel(AlertKind.Red, StaffErrors.InvalidCredentials.Message,
                $"After {lockout.MaxFailedAccessAttempts} wrong attempts the account is locked for {lockout.DefaultLockoutTimeSpan.TotalMinutes:0} minutes.");
            return Page();
        }

        await signIn.SignInAsync(HttpContext, login);
        return LocalRedirect(SafeReturnUrl);
    }

    /// <summary>The first validation message of a form field, or null.</summary>
    public string? FieldError(string key) =>
        ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0 ? entry.Errors[0].ErrorMessage : null;

    /// <summary>Where a rate-limited POST goes back to: the form again, with the return URL it had.</summary>
    public static string TooManyAttemptsUrl(HttpRequest request)
    {
        var query = new Dictionary<string, string?> { [TooManyAttemptsParameter] = "true" };
        var returnUrl = request.Query[ReturnUrlParameter].ToString();
        if (returnUrl.Length > 0)
            query[ReturnUrlParameter] = returnUrl;
        return QueryHelpers.AddQueryString(UiPaths.Login, query);
    }

    // Local URLs only (no "https://…", no "//host"), otherwise the start page.
    private string SafeReturnUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
}