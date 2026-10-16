using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Pages;

[AllowAnonymous]
public sealed class NotFoundModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status404NotFound;
}
