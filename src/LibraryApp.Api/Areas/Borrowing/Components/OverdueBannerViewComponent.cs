using LibraryApp.Application.Abstractions.Common;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// The red banner on My library when books are overdue (UI guide Part 1 "Group 3", demo <c>my-library.html</c>):
/// <c>&lt;vc:overdue-banner staff-member-id="@Model.StaffMemberId" /&gt;</c>. Renders nothing when no loan is overdue.
/// Only the member themselves or library staff see it (NFR-03); anyone else gets nothing.
/// </summary>
public sealed class OverdueBannerViewComponent(ICurrentUser currentUser) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid staffMemberId)
    {
        if (!StaffBorrowingReader.MaySee(User, currentUser, staffMemberId))
            return Content(string.Empty);

        return View("~/Areas/Borrowing/Components/OverdueBanner/Default.cshtml",
            await StaffBorrowingReader.ReadAsync(HttpContext, staffMemberId));
    }
}
