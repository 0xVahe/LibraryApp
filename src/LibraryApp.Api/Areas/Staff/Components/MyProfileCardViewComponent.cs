using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Staff.Components;

/// <summary>Only the caller's profile (MEM-08); no requested member id or HR-only details.</summary>
public sealed class MyProfileCardViewComponent(IStaffManagementService staff, ICurrentUser caller) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var profile = await MyProfileRead.GetAsync(HttpContext, staff, caller);
        return View("~/Areas/Staff/Components/MyProfileCard/Default.cshtml", profile);
    }
}
