using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Forms;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Borrowing.Pages;

/// <summary>
/// The Loan desk (UI guide Part 2, demo <c>desk.html</c>; start page of library staff): issue a copy by barcode to a
/// staff member found by employee ID or corporate email (BOR-01, Q-G3-06), take a copy back by barcode with the
/// "Damaged — send to repair" checkbox and a note (BOR-03, G3-21), issue the copies held for pickup (BOR-07, G3-20) and
/// Group 4's read-only overdue table (Q-G3-07). Success → Post/Redirect/Get with a green or amber <c>_Alert</c>.
/// <para>
/// Every form value here is typed or scanned at the desk, so a domain error — including "no copy with this barcode"
/// (NotFound) — is shown in red in the card of its form and the form stays filled. Forbidden still gets its page.
/// </para>
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class DeskModel(
    ILoanService loans,
    ILoanReturnService returns,
    IPickupListService pickups,
    ICatalogService catalog,
    IStaffMemberService staffMembers,
    IClock clock) : PageModel
{
    /// <summary>Model-state keys of the errors that belong to a whole card, not to one field.</summary>
    public const string IssueForm = "IssueForm";
    public const string ReturnForm = "ReturnForm";
    public const string PickupForm = "PickupForm";

    public string? Member { get; private set; }

    public string? Barcode { get; private set; }

    public string? ReturnBarcode { get; private set; }

    public bool Damaged { get; private set; }

    public string? Note { get; private set; }

    public PagedResult<PickupRow> Pickups { get; private set; } = null!;

    public async Task OnGetAsync([FromQuery(Name = PagerModel.PageParameter)] int page, CancellationToken ct) =>
        await LoadAsync(page, ct);

    /// <summary>Issue (BOR-01): the member by employee ID or corporate email, the copy by barcode.</summary>
    public async Task<IActionResult> OnPostIssueAsync(string? member, string? barcode, CancellationToken ct)
    {
        Member = member?.Trim();
        Barcode = barcode?.Trim();
        if (string.IsNullOrEmpty(Member))
            ModelState.AddModelError(nameof(Member), "Enter the employee ID or corporate email.");
        CheckBarcode(nameof(Barcode), Barcode);
        if (!ModelState.IsValid)
            return await PageAsync(ct);

        StaffMemberLookup? found = await staffMembers.FindAsync(Member!, ct);
        if (found is null)
        {
            ModelState.AddModelError(nameof(Member), "No staff member has this employee ID or corporate email.");
            return await PageAsync(ct);
        }

        try
        {
            LoanDto loan = await loans.IssueAsync(new IssueLoanRequest(found.Id, Barcode!), ct);
            await IssuedAsync(loan, found.FullName, ct);
            return RedirectToPage();
        }
        catch (DomainException ex) when (IsFormError(ex))
        {
            ModelState.AddModelError(IssueForm, ex.Error.Message);
            return await PageAsync(ct);
        }
    }

    /// <summary>Take back (BOR-03): the open loan of the copy (G3-21), then the return with its points (BOR-05, BOR-09).</summary>
    public async Task<IActionResult> OnPostReturnAsync(string? returnBarcode, ReturnLoanRequest request, CancellationToken ct)
    {
        ReturnBarcode = returnBarcode?.Trim();
        Damaged = request.Damaged;
        Note = request.Note;
        CheckBarcode(nameof(ReturnBarcode), ReturnBarcode);
        if (!ModelState.IsValid)
            return await PageAsync(ct);

        try
        {
            LoanDto loan = await loans.FindOpenByBarcodeAsync(ReturnBarcode!, ct);
            ReturnLoanResponse result = await returns.ReturnAsync(loan.LoanId, request, ct);
            await ReturnedAsync(loan, result, request.Damaged, ct);
            return RedirectToPage();
        }
        catch (DomainException ex) when (IsFormError(ex))
        {
            // The note rule of the loan (the same as ReturnLoanRequestValidator) belongs to the Note field, not to the card.
            ModelState.AddModelError(ex.Error == BorrowingErrors.ReturnNoteTooLong ? nameof(Note) : ReturnForm, ex.Error.Message);
            return await PageAsync(ct);
        }
    }

    /// <summary>
    /// "Issue" in the pickup table: the normal issue of the held copy to its member; the issue fulfils the reservation
    /// (BQ-BA-08, G3-02).
    /// </summary>
    public async Task<IActionResult> OnPostIssueHeldAsync(Guid staffMemberId, string? barcode, CancellationToken ct)
    {
        if (staffMemberId == Guid.Empty || string.IsNullOrWhiteSpace(barcode))
            return NotFound();

        try
        {
            LoanDto loan = await loans.IssueAsync(new IssueLoanRequest(staffMemberId, barcode), ct);
            IReadOnlyDictionary<Guid, string> names = await staffMembers.GetNamesAsync([staffMemberId], ct);
            await IssuedAsync(loan, names.GetValueOrDefault(staffMemberId, "the staff member"), ct);
            return RedirectToPage();
        }
        catch (DomainException ex) when (IsFormError(ex))
        {
            ModelState.AddModelError(PickupForm, ex.Error.Message);
            return await PageAsync(ct);
        }
    }

    /// <summary>The errors of a card or a field, for the view.</summary>
    public IReadOnlyList<string> ErrorsOf(string key) =>
        ModelState.TryGetValue(key, out var entry) ? entry.Errors.Select(e => e.ErrorMessage).ToList() : [];

    public string? InvalidOf(string key) => ErrorsOf(key).Count > 0 ? "true" : null;

    private async Task<IActionResult> PageAsync(CancellationToken ct)
    {
        await LoadAsync(1, ct);
        return Page();
    }

    /// <summary>The scanned barcode: required and as long as Catalog allows (the rule of <c>IssueLoanRequestValidator</c>).</summary>
    private void CheckBarcode(string field, string? barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            ModelState.AddModelError(field, "Enter the copy barcode.");
        else if (barcode.Length > BookCopy.BarcodeMaxLength)
            ModelState.AddModelError(field, $"The barcode may have at most {BookCopy.BarcodeMaxLength} characters.");
    }

    private static bool IsFormError(DomainException ex) =>
        ex.Error.Kind is ErrorKind.Validation or ErrorKind.Conflict or ErrorKind.BusinessRule or ErrorKind.NotFound;

    private async Task IssuedAsync(LoanDto loan, string memberName, CancellationToken ct)
    {
        var (title, barcode) = await CopyAsync(loan.BookCopyId, ct);
        TempData.SetAlert(AlertKind.Green, "Issued",
            $"{Texts.Quoted(title)} ({barcode}) to {memberName}. Due {Texts.Date(clock.ToLocalDate(loan.DueAtUtc))}.");
    }

    /// <summary>
    /// Green when on time and undamaged; amber with the working days late (BOR-05), the points and "goes to repair"
    /// otherwise (desk.html, corrected by the UI guide check: working days, no "Worn").
    /// </summary>
    private async Task ReturnedAsync(LoanDto loan, ReturnLoanResponse result, bool damaged, CancellationToken ct)
    {
        var (title, barcode) = await CopyAsync(loan.BookCopyId, ct);
        IReadOnlyDictionary<Guid, string> names = await staffMembers.GetNamesAsync([loan.StaffMemberId], ct);
        string text = $"{Texts.Quoted(title)} ({barcode}) from {names.GetValueOrDefault(loan.StaffMemberId, "the staff member")}.";
        if (result.PenaltyPointsApplied > 0)
            text += $" {Texts.Count(result.PenaltyPointsApplied, "penalty point")} added.";
        if (damaged)
            text += " The copy goes to repair.";

        if (result.OverdueWorkingDays > 0)
            TempData.SetAlert(AlertKind.Amber, $"Returned {Texts.Count(result.OverdueWorkingDays, "working day")} late", text);
        else if (damaged)
            TempData.SetAlert(AlertKind.Amber, "Returned damaged", text);
        else
            TempData.SetAlert(AlertKind.Green, "Returned", text);
    }

    private async Task<(string Title, string Barcode)> CopyAsync(Guid copyId, CancellationToken ct)
    {
        BookCopyDto? copy = (await catalog.GetCopiesAsync([copyId], ct)).SingleOrDefault();
        BookDto? book = copy is null ? null : await catalog.GetBookAsync(copy.BookId, ct);
        return (book?.Title ?? "Unknown book", copy?.Barcode ?? "unknown copy");
    }

    /// <summary>The pickup table: Borrowing's list, titles and barcodes from Catalog, names from Staff (G3-20, Q-G3-06).</summary>
    private async Task LoadAsync(int page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        PagedResult<ReadyForPickupDto> held = await pickups.GetReadyForPickupAsync(new PageRequest(page), ct);

        var titles = new Dictionary<Guid, string>();
        foreach (Guid bookId in held.Items.Select(r => r.BookId).Distinct())
            titles[bookId] = (await catalog.GetBookAsync(bookId, ct))?.Title ?? "Unknown book";

        Guid[] copyIds = held.Items.Select(r => r.CopyId).Distinct().ToArray();
        Dictionary<Guid, string> barcodes = copyIds.Length == 0
            ? []
            : (await catalog.GetCopiesAsync(copyIds, ct)).ToDictionary(c => c.Id, c => c.Barcode);
        IReadOnlyDictionary<Guid, string> names = await staffMembers.GetNamesAsync(held.Items.Select(r => r.StaffMemberId).Distinct().ToArray(), ct);

        var rows = held.Items.Select(r => new PickupRow(r.ReservationId, titles[r.BookId], r.StaffMemberId,
            names.GetValueOrDefault(r.StaffMemberId, "Unknown staff member"), barcodes.GetValueOrDefault(r.CopyId),
            clock.ToLocalDate(r.ReadyUntilUtc))).ToList();
        Pickups = new PagedResult<PickupRow>(rows, held.TotalCount, held.Page, held.PageSize);
    }
}

/// <summary>A copy held at the desk for its member (BOR-07); <see cref="Barcode"/> is null when Catalog lost the copy.</summary>
public sealed record PickupRow(Guid ReservationId, string Title, Guid StaffMemberId, string StaffName, string? Barcode, DateOnly HoldUntil);
