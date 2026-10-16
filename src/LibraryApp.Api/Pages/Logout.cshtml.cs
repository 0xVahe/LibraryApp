using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Pages;

/// <summary>
/// Sign-out: POST only. A live session is signed out only with the antiforgery token of its own layout form, so another
/// site cannot sign a user out. An ended session (the cookie expired, or a new security stamp rejected it) has nothing
/// left to protect: the request comes in anonymous, the old page's token no longer matches it, and refusing it would show
/// an error — so it just lands on the sign-in page.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]   // checked below, only while signed in
public sealed class LogoutModel(IUiSignIn signIn, IAntiforgery antiforgery) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true && !await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest();

        await signIn.SignOutAsync(HttpContext);
        return LocalRedirect(UiPaths.Login);
    }
}
