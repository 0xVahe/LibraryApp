using LibraryApp.Api.Ui.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Pages;

/// <summary>Start page: My library for a Staff Member, the Loan desk for a Librarian or a Library Administrator.</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect(UiNavigation.StartPath(User));
}
