using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>Staff search and pagination for library staff (MEM-07, NFR-06).</summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class IndexModel(IStaffManagementService staff, IDepartmentService departments) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public IReadOnlyList<DepartmentDto> Departments { get; private set; } = [];
    public PagedResult<StaffMemberListItemDto> Result { get; private set; } =
        PagedResult<StaffMemberListItemDto>.Empty(new PageRequest());

    public bool HasFilters => !string.IsNullOrWhiteSpace(Search) || DepartmentId.HasValue || !string.IsNullOrWhiteSpace(Status);

    public string? Invalid(string field) =>
        ModelState.TryGetValue(field, out var entry) && entry.Errors.Count > 0 ? "true" : null;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Departments = await departments.ListAsync(ct);
        if (!ModelState.IsValid)
            return Page();

        var page = int.TryParse(Request.Query[PagerModel.PageParameter], out var requestedPage) ? requestedPage : 1;
        try
        {
            Result = await staff.SearchAsync(new StaffMemberSearchRequest(Search, DepartmentId, Status, new PageRequest(page)), ct);
            return Page();
        }
        catch (DomainException exception)
        {
            if (exception.Error == StaffErrors.StatusInvalid)
            {
                ModelState.AddModelError(nameof(Status), exception.Error.Message);
                return Page();
            }
            return this.DomainError(exception);
        }
    }
}
