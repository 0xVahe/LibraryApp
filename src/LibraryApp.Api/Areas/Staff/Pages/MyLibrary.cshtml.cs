using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>MEM-08: the signed-in caller's library, for every role (NFR-03).</summary>
public sealed class MyLibraryModel(IStaffManagementService staff, ICurrentUser caller) : PageModel
{
    public MyProfileDto Profile { get; private set; } = null!;
    public StaffBorrowingView Borrowing { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Profile = await MyProfileRead.GetAsync(HttpContext, staff, caller);
        Borrowing = await StaffBorrowingReader.ReadAsync(HttpContext, Profile.Id);
    }
}
