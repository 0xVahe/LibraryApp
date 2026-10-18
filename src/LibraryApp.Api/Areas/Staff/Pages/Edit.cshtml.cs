using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Staff.Pages;

[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class EditModel(
    IStaffManagementService staff,
    IDepartmentService departments,
    IStaffCategoryService categories) : StaffFormPage(departments, categories)
{
    [BindProperty]
    public UpdateStaffMemberRequest Input { get; set; } = new("", "", "", Guid.Empty, "", null, Guid.Empty);

    public StaffMemberDto Member { get; private set; } = null!;

    public async Task OnGetAsync(Guid id, CancellationToken ct)
    {
        Member = await staff.GetAsync(id, ct);
        await LoadListsAsync(ct);
        Input = new(Member.FirstName, Member.LastName, Member.CorporateEmail, Member.DepartmentId,
            Member.Position, Member.LineManagerEmail, Member.StaffCategoryId);
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        // Reload display-only fields from the service; posted EmployeeId and Role values have no effect.
        Member = await staff.GetAsync(id, ct);
        await LoadListsAsync(ct);
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await staff.UpdateAsync(id, Input, ct);
            TempData.SetAlert(AlertKind.Green, "Staff member updated.");
            return RedirectToPage("./Edit", new { id });
        }
        catch (DomainException exception)
        {
            return FormError(exception);
        }
    }
}
