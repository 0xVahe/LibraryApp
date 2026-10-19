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

/// <summary>Staff details and administrative deactivation (MEM-01, BR-MEM-4, NFR-06).</summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class DetailsModel(IStaffManagementService staff, IStaffDeactivationService deactivation) : PageModel
{
    [BindProperty]
    public DeactivateStaffMemberRequest Input { get; set; } = new("");

    public StaffMemberDto Member { get; private set; } = null!;
    public IReadOnlyList<OutstandingBookDto> OutstandingBooks { get; private set; } = [];
    public string FullName => $"{Member.FirstName} {Member.LastName}";
    public string Initials => string.Concat(Member.FirstName.Take(1).Concat(Member.LastName.Take(1))).ToUpperInvariant();
    public StaffMemberStatus Status => Enum.Parse<StaffMemberStatus>(Member.Status);
    public bool IsDeactivated => Status == StaffMemberStatus.Deactivated;
    public string? ReasonInvalid =>
        ModelState.TryGetValue("Input.Reason", out var entry) && entry.Errors.Count > 0 ? "true" : null;

    public async Task OnGetAsync(Guid id, CancellationToken ct) => await LoadAsync(id, ct);

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await deactivation.DeactivateAsync(id, Input, ct);
            TempData.SetAlert(AlertKind.Green, "Staff member deactivated.", "Their sign-in is disabled and their history is preserved.");
            return RedirectToPage("./Details", new { id });
        }
        catch (DomainException exception)
        {
            // A concurrent deactivation may have changed the profile while this form was open.
            await LoadAsync(id, ct);
            if (exception.Error == StaffErrors.HasActiveLoans &&
                exception.Extensions.TryGetValue("outstandingBooks", out var books) && books is IReadOnlyList<OutstandingBookDto> list)
                OutstandingBooks = list;
            return this.DomainError(exception);
        }
    }

    private async Task LoadAsync(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Member = await staff.GetAsync(id, ct);
    }
}
