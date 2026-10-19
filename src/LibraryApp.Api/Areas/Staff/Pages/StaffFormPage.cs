using LibraryApp.Api.Ui.Forms;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>Lookups and field errors shared by the Staff enrol and edit forms.</summary>
public abstract class StaffFormPage(IDepartmentService departments, IStaffCategoryService categories) : PageModel
{
    public IReadOnlyList<DepartmentDto> Departments { get; private set; } = [];
    public IReadOnlyList<StaffCategoryDto> Categories { get; private set; } = [];

    public string? Invalid(string field) =>
        ModelState.TryGetValue($"Input.{field}", out var entry) && entry.Errors.Count > 0 ? "true" : null;

    protected async Task LoadListsAsync(CancellationToken ct)
    {
        // These services share a scoped DbContext; keep their queries sequential.
        Departments = await departments.ListAsync(ct);
        Categories = await categories.GetListAsync(ct);
        Response.Headers.CacheControl = "no-store";
    }

    protected IActionResult FormError(DomainException exception)
    {
        var field = exception.Error.Code switch
        {
            var code when code == StaffErrors.EmployeeIdTaken.Code => nameof(EnrolStaffMemberRequest.EmployeeId),
            var code when code == StaffErrors.EmailTaken.Code || code == StaffErrors.EnrolEmailNotCorporate.Code =>
                nameof(EnrolStaffMemberRequest.CorporateEmail),
            var code when code == StaffErrors.DepartmentInvalid.Code => nameof(EnrolStaffMemberRequest.DepartmentId),
            var code when code == StaffErrors.CategoryNotFound.Code => nameof(EnrolStaffMemberRequest.StaffCategoryId),
            _ => null,
        };

        if (field is null)
            return this.DomainError(exception);

        ModelState.AddModelError($"Input.{field}", exception.Error.Message);
        return Page();
    }
}
