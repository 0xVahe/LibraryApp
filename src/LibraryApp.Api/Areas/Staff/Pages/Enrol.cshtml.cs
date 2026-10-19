using System.Security.Cryptography;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Staff.Pages;

[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class EnrolModel(
    IStaffManagementService staff,
    IDepartmentService departments,
    IStaffCategoryService categories) : StaffFormPage(departments, categories)
{
    [BindProperty]
    public EnrolStaffMemberRequest Input { get; set; } =
        new("", "", "", "", Guid.Empty, "", null, Guid.Empty, "", StaffRoles.StaffMember);

    public bool CanAssignRoles => User.IsInRole(StaffRoles.LibraryAdministrator);

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadListsAsync(ct);
        Input = Input with { Password = $"A{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}9" };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadListsAsync(ct);
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var member = await staff.EnrolAsync(Input, ct);
            TempData.SetAlert(AlertKind.Green, "Staff member enrolled.");
            return RedirectToPage("./Edit", new { id = member.Id });
        }
        catch (DomainException exception)
        {
            return FormError(exception);
        }
    }
}
