using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// "Loans and reservations" and "Standing" on Group 2's Staff member page (UI guide Part 2, demo <c>staff-member.html</c>):
/// the page invokes it as <c>Component.InvokeAsync("StaffBorrowingSummary", new { staffMemberId })</c>. Library staff only,
/// like the page (anyone else gets nothing). Each open loan has "Declare lost" (posted to <see cref="Pages.DeclareLostModel"/>); each active penalty record a waiver form (posted to <see cref="Pages.WaivePenaltyModel"/>); the Loans card links to the Loan history page (<see cref="Pages.LoanHistoryModel"/>).
/// </summary>
public sealed class StaffBorrowingSummaryViewComponent : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid staffMemberId)
    {
        if (!(User.IsInRole(StaffRoles.Librarian) || User.IsInRole(StaffRoles.LibraryAdministrator)))
            return Content(string.Empty);

        return View("~/Areas/Borrowing/Components/StaffBorrowingSummary/Default.cshtml",
            await StaffBorrowingReader.ReadAsync(HttpContext, staffMemberId));
    }
}
