using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Staff.Pages;

/// <summary>Administrator settings for departments and staff borrowing categories.</summary>
[Authorize(Policy = UiPolicies.Administrator)]
public sealed class SettingsModel(IDepartmentService departments, IStaffCategoryService categories) : PageModel
{
    public IReadOnlyList<DepartmentSettingsDto> Departments { get; private set; } = [];
    public IReadOnlyList<StaffCategoryDto> Categories { get; private set; } = [];

    public UpsertDepartmentRequest DepartmentInput { get; private set; } = new(null);
    public UpsertDepartmentRequest DepartmentEditInput { get; private set; } = new(null);
    public Guid? EditingDepartmentId { get; private set; }

    public UpsertStaffCategoryRequest CategoryInput { get; private set; } = new("", 1, 1, 0);
    public UpsertStaffCategoryRequest CategoryEditInput { get; private set; } = new("", 1, 1, 0);
    public Guid? EditingCategoryId { get; private set; }
    public bool ShowCategoryForm { get; private set; }

    private string? ActiveForm { get; set; }

    public string? ErrorFor(string form, string field) => ActiveForm == form
        && ModelState.TryGetValue(field, out var entry)
        && entry.Errors.Count > 0
            ? entry.Errors[0].ErrorMessage
            : null;

    public string? Invalid(string form, string field) => ErrorFor(form, field) is not null ? "true" : null;

    public async Task OnGetAsync(Guid? editDepartmentId, Guid? editCategoryId, bool addCategory, CancellationToken ct)
    {
        EditingDepartmentId = editDepartmentId;
        EditingCategoryId = editCategoryId;
        ShowCategoryForm = addCategory;

        if (EditingDepartmentId is Guid departmentId)
            DepartmentEditInput = new UpsertDepartmentRequest((await departments.GetAsync(departmentId, ct)).Name);
        await LoadEditingCategoryAsync(ct);
        await LoadAsync(ct);
    }

    public string SettingsUrl(Guid? editDepartmentId = null, Guid? editCategoryId = null, bool addCategory = false)
    {
        var query = new List<string>();
        if (editDepartmentId is Guid departmentId)
            query.Add($"editDepartmentId={departmentId}");
        if (editCategoryId is Guid categoryId)
            query.Add($"editCategoryId={categoryId}");
        if (addCategory)
            query.Add("addCategory=true");
        return query.Count == 0 ? "/settings" : $"/settings?{string.Join("&", query)}";
    }

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
            ModelState.AddModelError(nameof(request.Name), exception.Error.Message);
            return this.DomainError(exception);
        }
    }

    public async Task<IActionResult> OnPostCreateCategoryAsync(
        UpsertStaffCategoryRequest request,
        Guid? editingCategoryId,
        CancellationToken ct)
    {
        ActiveForm = "category-create";
        ShowCategoryForm = true;
        EditingCategoryId = editingCategoryId;
        CategoryInput = request;
        NormalizeEmptyCategoryName();
        if (!ModelState.IsValid)
        {
            await LoadEditingCategoryAsync(ct);
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
            await LoadEditingCategoryAsync(ct);
            await LoadAsync(ct);
            var hasFieldError = AddCategoryErrorToField(exception);
            var result = this.DomainError(exception);
            if (hasFieldError)
                ModelState.Remove(string.Empty);
            return result;
        }
    }

    public async Task<IActionResult> OnPostUpdateCategoryAsync(
        Guid id,
        UpsertStaffCategoryRequest request,
        bool showCategoryForm,
        CancellationToken ct)
    {
        ActiveForm = "category-edit";
        EditingCategoryId = id;
        ShowCategoryForm = showCategoryForm;
        CategoryEditInput = request;
        NormalizeEmptyCategoryName();
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
            var hasFieldError = AddCategoryErrorToField(exception);
            var result = this.DomainError(exception);
            if (hasFieldError)
                ModelState.Remove(string.Empty);
            return result;
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Departments = await departments.ListForSettingsAsync(ct);
        Categories = await categories.GetListAsync(ct);
        Response.Headers.CacheControl = "no-store";
    }

    private async Task LoadEditingCategoryAsync(CancellationToken ct)
    {
        if (EditingCategoryId is not Guid id)
            return;

        var category = await categories.GetAsync(id, ct);
        CategoryEditInput = new UpsertStaffCategoryRequest(
            category.Name, category.MaxLoans, category.LoanDays, category.MaxRenewals);
    }

    private void NormalizeEmptyCategoryName()
    {
        var name = ActiveForm == "category-edit" ? CategoryEditInput.Name : CategoryInput.Name;
        if (!string.IsNullOrWhiteSpace(name))
            return;

        ModelState.Remove(nameof(UpsertStaffCategoryRequest.Name));
        ModelState.AddModelError(nameof(UpsertStaffCategoryRequest.Name), StaffErrors.CategoryNameRequired.Message);
    }

    private bool AddCategoryErrorToField(DomainException exception)
    {
        var field = exception.Error.Code switch
        {
            var code when code == StaffErrors.CategoryNameTaken.Code || code == StaffErrors.CategoryNameRequired.Code => nameof(UpsertStaffCategoryRequest.Name),
            var code when code == StaffErrors.MaxLoansInvalid.Code => nameof(UpsertStaffCategoryRequest.MaxLoans),
            var code when code == StaffErrors.LoanDaysInvalid.Code => nameof(UpsertStaffCategoryRequest.LoanDays),
            var code when code == StaffErrors.MaxRenewalsInvalid.Code => nameof(UpsertStaffCategoryRequest.MaxRenewals),
            _ => null,
        };
        if (field is not null)
            ModelState.AddModelError(field, exception.Error.Message);
        return field is not null;
    }
}
