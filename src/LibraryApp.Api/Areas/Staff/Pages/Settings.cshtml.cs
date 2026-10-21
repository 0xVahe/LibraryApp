using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>Administrator settings for departments and staff borrowing categories.</summary>
[Authorize(Policy = UiPolicies.Administrator)]
public sealed class SettingsModel(IDepartmentService departments, IStaffCategoryService categories) : PageModel
{
    public IReadOnlyList<DepartmentDto> Departments { get; private set; } = [];
    public IReadOnlyList<StaffCategoryDto> Categories { get; private set; } = [];

    public UpsertDepartmentRequest DepartmentInput { get; private set; } = new(null);
    public UpsertDepartmentRequest DepartmentEditInput { get; private set; } = new(null);
    public Guid? EditingDepartmentId { get; private set; }

    public UpsertStaffCategoryRequest CategoryInput { get; private set; } = new("", 1, 1, 0);
    public UpsertStaffCategoryRequest CategoryEditInput { get; private set; } = new("", 1, 1, 0);
    public Guid? EditingCategoryId { get; private set; }

    private string? ActiveForm { get; set; }

    public string? ErrorFor(string form, string field) => ActiveForm == form
        && ModelState.TryGetValue(field, out var entry)
        && entry.Errors.Count > 0
            ? entry.Errors[0].ErrorMessage
            : null;

    public string? Invalid(string form, string field) => ErrorFor(form, field) is not null ? "true" : null;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateDepartmentAsync(UpsertDepartmentRequest request, CancellationToken ct)
    {
        ActiveForm = "department-create";
        DepartmentInput = request;
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await departments.CreateAsync(request, ct);
            TempData.SetAlert(AlertKind.Green, "Department added.");
            return RedirectToPage();
        }
        catch (DomainException exception)
        {
            await LoadAsync(ct);
            return this.DomainError(exception);
        }
    }

    public async Task<IActionResult> OnPostRenameDepartmentAsync(Guid id, UpsertDepartmentRequest request, CancellationToken ct)
    {
        ActiveForm = "department-edit";
        EditingDepartmentId = id;
        DepartmentEditInput = request;
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await departments.UpdateAsync(id, request, ct);
            TempData.SetAlert(AlertKind.Green, "Department renamed.");
            return RedirectToPage();
        }
        catch (DomainException exception)
        {
            await LoadAsync(ct);
            return this.DomainError(exception);
        }
    }

    public async Task<IActionResult> OnPostCreateCategoryAsync(UpsertStaffCategoryRequest request, CancellationToken ct)
    {
        ActiveForm = "category-create";
        CategoryInput = request;
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await categories.CreateAsync(request, ct);
            TempData.SetAlert(AlertKind.Green, "Staff category added.");
            return RedirectToPage();
        }
        catch (DomainException exception)
        {
            await LoadAsync(ct);
            return this.DomainError(exception);
        }
    }

    public async Task<IActionResult> OnPostUpdateCategoryAsync(Guid id, UpsertStaffCategoryRequest request, CancellationToken ct)
    {
        ActiveForm = "category-edit";
        EditingCategoryId = id;
        CategoryEditInput = request;
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await categories.UpdateAsync(id, request, ct);
            TempData.SetAlert(AlertKind.Green, "Staff category updated.");
            return RedirectToPage();
        }
        catch (DomainException exception)
        {
            await LoadAsync(ct);
            return this.DomainError(exception);
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Departments = await departments.ListAsync(ct);
        Categories = await categories.GetListAsync(ct);
        Response.Headers.CacheControl = "no-store";
    }
}
