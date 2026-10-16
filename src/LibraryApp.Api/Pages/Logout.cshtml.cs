using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Pages;

/// <summary>
/// Sign-out: POST only, with the antiforgery token of the layout's form, so another site cannot sign a user out.
/// Anonymous, so an expired session still lands on the sign-in page.
/// </summary>
[AllowAnonymous]
public sealed class LogoutModel(IUiSignIn signIn) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync(HttpContext);
        return LocalRedirect(UiPaths.Login);
    }
}
