using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>MEM-08: the signed-in caller's library, for every role (NFR-03).</summary>
public sealed class MyLibraryModel(IMyLibraryService library) : PageModel
{
    public MyLibraryDto Library { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Library = await MyLibraryRead.GetAsync(HttpContext, library);
    }
}
