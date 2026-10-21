using LibraryApp.Api.Areas.Borrowing.Components;
using LibraryApp.Api.Ui;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LibraryApp.Api.Areas.Borrowing.Pages;

/// <summary>
/// Loan history of one staff member (BOR-10; UI guide Part 2 "Loan history", demo <c>staff-loans.html</c>): every
/// loan, also Returned and Lost, newest issue first, filtered by status (the effective one: an Active loan past the overdue
/// rule is Overdue, in the badge and in the filter) and by issue period through the query string, with
/// the shared pager. Library staff only; an unknown staff member is the Not found page. Data from the loan history service, titles
/// and barcodes from Catalog, the name from Staff and the points of each loan from the penalty records (waived ones left
/// out: they no longer count).
/// </summary>
[Authorize(Policy = UiPolicies.LibraryStaff)]
public sealed class LoanHistoryModel(
    ILoanHistoryService history,
    IPenaltyAdministrationService penalties,
    ICatalogService catalog,
    IStaffMemberService staffMembers,
    IClock clock) : PageModel
{
    /// <summary>The statuses of the filter, in the order of the demo; the text is the badge text ("On loan").</summary>
    public static readonly IReadOnlyList<LoanStatus> Statuses = [LoanStatus.Active, LoanStatus.Overdue, LoanStatus.Returned, LoanStatus.Lost];

    public Guid StaffMemberId { get; private set; }

    public string StaffName { get; private set; } = "";

    [BindProperty(SupportsGet = true)]
    public LoanStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public PagedResult<HistoryRow> Loans { get; private set; } = new([], 0, 1, PageRequest.DefaultPageSize);

    public bool Filtered => Status is not null || From is not null || To is not null;

    public string StaffPath => $"{UiNavigation.Staff}/{StaffMemberId}";

    public string HistoryPath => $"{StaffPath}/loans";

    public async Task<IActionResult> OnGetAsync(Guid id, [FromQuery(Name = PagerModel.PageParameter)] int page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        IReadOnlyDictionary<Guid, string> names = await staffMembers.GetNamesAsync([id], ct);
        if (!names.TryGetValue(id, out string? name))
            return NotFound();

        StaffMemberId = id;
        StaffName = name;

        // A status the filter does not know (?Status=Lent) is not bound: the page shows every status.
        ModelState.Remove(nameof(Status));
        if (From is { } from && To is { } to && from > to)
        {
            ModelState.AddModelError(nameof(To), BorrowingErrors.HistoryPeriodInvalid.Message);
            return Page();
        }

        PagedResult<LoanHistoryItemDto> loans = await history.ListAsync(
            new LoanHistoryFilter(id, null, Status, From, To), new PageRequest(page), ct);
        Loans = await RowsAsync(id, loans, ct);
        return Page();
    }

    /// <summary>"6 loans · 1 Jan 2026 – 17 Oct 2026", "6 loans since 1 Jan 2026", "6 loans".</summary>
    public string CountText => Texts.Count(Loans.TotalCount, "loan") + (From, To) switch
    {
        ({ } from, { } to) => $" · {Texts.Date(from)} – {Texts.Date(to)}",
        ({ } from, null) => $" since {Texts.Date(from)}",
        (null, { } to) => $" until {Texts.Date(to)}",
        _ => "",
    };

    public IReadOnlyList<string> ErrorsOf(string key) =>
        ModelState.TryGetValue(key, out var entry) ? entry.Errors.Select(e => e.ErrorMessage).ToList() : [];

    private async Task<PagedResult<HistoryRow>> RowsAsync(Guid staffMemberId, PagedResult<LoanHistoryItemDto> loans, CancellationToken ct)
    {
        Guid[] copyIds = loans.Items.Select(l => l.BookCopyId).Distinct().ToArray();
        IReadOnlyList<BookCopyDto> copies = copyIds.Length == 0 ? [] : await catalog.GetCopiesAsync(copyIds, ct);
        var copyById = copies.ToDictionary(c => c.Id);

        var titles = new Dictionary<Guid, string>();
        foreach (Guid bookId in copies.Select(c => c.BookId).Distinct())
            titles[bookId] = (await catalog.GetBookAsync(bookId, ct))?.Title ?? "Unknown book";

        Dictionary<Guid, int> points = loans.Items.Count == 0 ? [] : await PointsByLoanAsync(staffMemberId, ct);

        var rows = loans.Items.Select(loan =>
        {
            BookCopyDto? copy = copyById.GetValueOrDefault(loan.BookCopyId);
            return new HistoryRow(
                copy is null ? "Unknown book" : titles[copy.BookId],
                copy?.Barcode,
                clock.ToLocalDate(loan.IssuedAtUtc),
                clock.ToLocalDate(loan.DueAtUtc),
                loan.Status == LoanStatus.Returned && loan.ReturnedAtUtc is { } returned ? clock.ToLocalDate(returned) : null,
                loan.Status,   // effective: the history gives an Active loan past the overdue rule as Overdue
                points.GetValueOrDefault(loan.LoanId));
        }).ToList();
        return new PagedResult<HistoryRow>(rows, loans.TotalCount, loans.Page, loans.PageSize);
    }

    /// <summary>The points each loan gave (late, damaged, lost), from every penalty record of the member except waived ones.</summary>
    private async Task<Dictionary<Guid, int>> PointsByLoanAsync(Guid staffMemberId, CancellationToken ct)
    {
        var points = new Dictionary<Guid, int>();
        for (int page = 1; ; page++)
        {
            PagedResult<PenaltyDto> records = await penalties.ListAsync(staffMemberId, null, new PageRequest(page, PageRequest.MaxPageSize), ct);
            foreach (PenaltyDto record in records.Items.Where(r => r.Status != PenaltyRecordStatus.Waived))
                points[record.LoanId] = points.GetValueOrDefault(record.LoanId) + record.Points;
            if (page >= records.TotalPages)
                return points;
        }
    }
}

/// <summary>One row of the history; <see cref="Returned"/> is set for a Returned loan only, <see cref="Points"/> 0 is "—".</summary>
public sealed record HistoryRow(string Title, string? Barcode, DateOnly Issued, DateOnly Due, DateOnly? Returned, LoanStatus Status, int Points);
