using LibraryApp.Application.Staff.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Staff.Components;

/// <summary>Only the caller's profile (MEM-08); no requested member id or HR-only details.</summary>
public sealed class MyProfileCardViewComponent(IMyLibraryService library) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var summary = await MyLibraryRead.GetAsync(HttpContext, library);
        return View("~/Areas/Staff/Components/MyProfileCard/Default.cshtml", summary.Profile);
    }
}
