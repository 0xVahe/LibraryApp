using LibraryApp.Application.Abstractions.Common;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// "Penalties" on My library: the active penalty records (BOR-05, penalty-point policy; demo <c>my-library.html</c>):
/// <c>&lt;vc:my-penalties staff-member-id="@Model.StaffMemberId" /&gt;</c>.
/// Only the member themselves or library staff see it (NFR-03); anyone else gets nothing.
/// </summary>
public sealed class MyPenaltiesViewComponent(ICurrentUser currentUser) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid staffMemberId)
    {
        if (!StaffBorrowingReader.MaySee(User, currentUser, staffMemberId))
            return Content(string.Empty);

        return View("~/Areas/Borrowing/Components/MyPenalties/Default.cshtml",
            await StaffBorrowingReader.ReadAsync(HttpContext, staffMemberId));
    }
}
