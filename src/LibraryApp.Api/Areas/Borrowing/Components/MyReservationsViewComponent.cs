using LibraryApp.Application.Abstractions.Common;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// "Reservations" on My library with the Cancel button (BOR-07, BOR-12; demo <c>my-library.html</c>):
/// <c>&lt;vc:my-reservations staff-member-id="@Model.StaffMemberId" /&gt;</c>. The cancellation is posted to
/// <see cref="Pages.CancelReservationModel"/>.
/// Only the member themselves or library staff see it (NFR-03); anyone else gets nothing.
/// </summary>
public sealed class MyReservationsViewComponent(ICurrentUser currentUser) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid staffMemberId)
    {
        if (!StaffBorrowingReader.MaySee(User, currentUser, staffMemberId))
            return Content(string.Empty);

        return View("~/Areas/Borrowing/Components/MyReservations/Default.cshtml",
            await StaffBorrowingReader.ReadAsync(HttpContext, staffMemberId));
    }
}
