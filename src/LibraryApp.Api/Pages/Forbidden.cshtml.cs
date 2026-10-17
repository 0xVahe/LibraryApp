using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Pages;

/// <summary>Where the cookie scheme sends a signed-in user whose role does not allow the page (AccessDeniedPath).</summary>
[AllowAnonymous]
public sealed class ForbiddenModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
