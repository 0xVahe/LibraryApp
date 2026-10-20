using LibraryApp.Application.Abstractions.Common;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// "Books on loan" on My library with the Renew button (BOR-04; UI guide Part 1 "Group 3", demo <c>my-library.html</c>):
/// <c>&lt;vc:my-loans staff-member-id="@Model.StaffMemberId" /&gt;</c>. The renewal is posted to <see cref="Pages.RenewLoanModel"/>.
/// Only the member themselves or library staff see it (NFR-03); anyone else gets nothing.
/// </summary>
public sealed class MyLoansViewComponent(ICurrentUser currentUser) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid staffMemberId)
    {
        if (!StaffBorrowingReader.MaySee(User, currentUser, staffMemberId))
            return Content(string.Empty);

        return View("~/Areas/Borrowing/Components/MyLoans/Default.cshtml",
            await StaffBorrowingReader.ReadAsync(HttpContext, staffMemberId));
    }
}
