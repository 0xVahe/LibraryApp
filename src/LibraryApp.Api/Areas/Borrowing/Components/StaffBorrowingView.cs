using System.Globalization;
using System.Security.Principal;
using LibraryApp.Api.Areas.Catalog;
using LibraryApp.Api.Ui.Shared;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// Everything the Borrowing blocks of one staff member show (My library: <c>OverdueBanner</c>, <c>MyLoans</c>,
/// <c>MyReservations</c>, <c>MyPenalties</c>; Staff member: <c>StaffBorrowingSummary</c>), read once per request:
/// several blocks on one page share it (<see cref="StaffBorrowingReader.ReadAsync"/>).
/// </summary>
public sealed record StaffBorrowingView(
    Guid StaffMemberId,
    IReadOnlyList<LoanLine> Loans,
    IReadOnlyList<ReservationLine> Reservations,
    IReadOnlyList<PenaltyLine> Penalties,
    int ActivePenaltyPoints,
    LendingRules Rules)
{
    public IReadOnlyList<LoanLine> OverdueLoans => Loans.Where(loan => loan.IsOverdue).ToList();
}

/// <summary>A book as the Borrowing blocks show it; <see cref="Href"/> is null for a book Catalog no longer knows.</summary>
public sealed record BorrowedBook(Guid BookId, string Title, string? Authors, BookCoverModel Cover, string? Href);

/// <summary>
/// An open loan. <c>Due</c> is the local (Asia/Yerevan) date; <c>IsOverdue</c>: marked Overdue, or past the overdue rule
/// before the daily job marked it (BQ-BA-05); <c>OverdueWorkingDays</c>: working days since the due date (BOR-05), 0 when
/// not overdue; <c>DaysLeft</c>: calendar days until the due date, negative when it has passed.
/// </summary>
public sealed record LoanLine(
    Guid LoanId,
    BorrowedBook Book,
    string? Barcode,
    DateOnly Due,
    bool IsOverdue,
    int OverdueWorkingDays,
    int DaysLeft,
    int RenewalCount)
{
    /// <summary>
    /// "Due in N days" in amber from this many days before the due date (demo <c>my-library.html</c>: due in 5 days is
    /// amber); before that the loan is "On loan" in blue (design.md, "Badges").
    /// </summary>
    public const int DueSoonDays = 5;

    /// <summary>Overdue in red (in working days, BOR-05), due soon in amber, otherwise "On loan" in blue.</summary>
    public BadgeModel Badge => IsOverdue
        ? StatusBadge.Custom(OverdueWorkingDays > 0 ? $"Overdue · {Texts.Count(OverdueWorkingDays, "working day")}" : "Overdue", "red")
        : DaysLeft <= DueSoonDays
            ? StatusBadge.Custom(DaysLeft switch { <= 0 => "Due today", 1 => "Due tomorrow", var n => $"Due in {n} days" }, "amber")
            : StatusBadge.For(LoanStatus.Active);

    /// <summary>The status of the loan for the staff table: Overdue (red) or On loan (blue).</summary>
    public LoanStatus Status => IsOverdue ? LoanStatus.Overdue : LoanStatus.Active;

    /// <summary>
    /// The Renew button: not on an overdue loan and not after the last renewal (BR-BOR-1). The other refusals (status,
    /// someone waiting) come from the service as a red alert.
    /// </summary>
    public bool CanRenew(LendingRules rules) => !IsOverdue && RenewalCount < rules.MaxRenewals;

    /// <summary>"Robert C. Martin · Copy BC-000118".</summary>
    public string CopyText => string.Join(" · ", new[] { Book.Authors, Barcode is null ? null : $"Copy {Barcode}" }.OfType<string>());

    public string DueText(LendingRules rules) =>
        $"Due {Texts.Date(Due)} · " +
        (rules.MaxRenewals == 0 ? "no renewals" : $"renewed {RenewalCount} of {Texts.Count(rules.MaxRenewals, "time")}");
}

/// <summary>A Waiting or ReadyForPickup reservation; <c>QueuePosition</c> is 1..n while Waiting, 0 when ReadyForPickup.</summary>
public sealed record ReservationLine(
    Guid ReservationId,
    BorrowedBook Book,
    ReservationStatus Status,
    int QueuePosition,
    DateOnly? ReadyUntil,
    DateOnly Reserved)
{
    /// <summary>"Pick up before 18 Oct 2026 at the library desk" or "Reserved 2 Oct 2026 · you are 2nd in the queue".</summary>
    public string Text(bool toMember) => Status == ReservationStatus.ReadyForPickup && ReadyUntil is { } until
        ? $"{(toMember ? "Pick up" : "Held")} before {Texts.Date(until)} at the library desk"
        : $"Reserved {Texts.Date(Reserved)} · {(toMember ? "you are " : "")}{Texts.Ordinal(QueuePosition)} in the queue";
}

public sealed record PenaltyLine(Guid PenaltyId, BorrowedBook Book, int Points, string Reason, DateOnly Expires);

/// <summary>
/// Reads <see cref="StaffBorrowingView"/>: the Borrowing summary (<see cref="IBorrowingReadService"/>), titles and
/// barcodes from Catalog, the member's loan limits from Staff (cross-module interfaces only, UI guide Part 0).
/// </summary>
public sealed class StaffBorrowingReader(
    IBorrowingReadService borrowing,
    ICatalogService catalog,
    IStaffMemberService staffMembers,
    IWorkingDayCalendar calendar,
    OverdueRule overdueRule,
    IClock clock)
{
    /// <summary>
    /// The view of one member, read once per request: the blocks of one page share it. View components are created
    /// by the container, so the reader is built here instead of being registered by the shared host.
    /// </summary>
    public static Task<StaffBorrowingView> ReadAsync(HttpContext http, Guid staffMemberId)
    {
        string key = $"borrowing.staff-view:{staffMemberId:N}";
        if (http.Items[key] is Task<StaffBorrowingView> read)
            return read;

        var reader = ActivatorUtilities.CreateInstance<StaffBorrowingReader>(http.RequestServices);
        read = reader.LoadAsync(staffMemberId, http.RequestAborted);
        http.Items[key] = read;
        return read;
    }

    /// <summary>Own data only (NFR-03): the member themselves, or a Librarian or Library Administrator.</summary>
    public static bool MaySee(IPrincipal user, ICurrentUser currentUser, Guid staffMemberId) =>
        user.IsInRole(StaffRoles.Librarian) || user.IsInRole(StaffRoles.LibraryAdministrator)
        || (user.Identity?.IsAuthenticated == true && currentUser.StaffMemberId == staffMemberId);

    public async Task<StaffBorrowingView> LoadAsync(Guid staffMemberId, CancellationToken ct)
    {
        StaffBorrowingSummary summary = await borrowing.GetStaffSummaryAsync(staffMemberId, ct);
        LendingRules rules = await staffMembers.GetLendingRulesAsync(staffMemberId, ct);

        var books = new Dictionary<Guid, BorrowedBook>();
        foreach (Guid bookId in summary.ActiveLoans.Select(l => l.BookId)
                     .Concat(summary.Reservations.Select(r => r.BookId))
                     .Concat(summary.ActivePenalties.Select(p => p.BookId))
                     .Distinct())
            books[bookId] = Describe(bookId, await catalog.GetBookAsync(bookId, ct));

        Guid[] copyIds = summary.ActiveLoans.Select(l => l.BookCopyId).Distinct().ToArray();
        Dictionary<Guid, string> barcodes = copyIds.Length == 0
            ? []
            : (await catalog.GetCopiesAsync(copyIds, ct)).ToDictionary(c => c.Id, c => c.Barcode);

        DateTime now = clock.UtcNow;
        DateOnly today = clock.LocalToday;

        var loans = summary.ActiveLoans.Select(loan =>
        {
            DateOnly due = clock.ToLocalDate(loan.DueAtUtc);
            bool overdue = loan.Status == LoanStatus.Overdue || overdueRule.IsOverdue(loan.DueAtUtc, now);
            return new LoanLine(loan.LoanId, books[loan.BookId], barcodes.GetValueOrDefault(loan.BookCopyId), due, overdue,
                overdue ? calendar.CountWorkingDays(due, today) : 0, due.DayNumber - today.DayNumber, loan.RenewalCount);
        }).ToList();

        var reservations = summary.Reservations.Select(r => new ReservationLine(r.ReservationId, books[r.BookId], r.Status,
            r.QueuePosition, r.ReadyUntilUtc is { } until ? clock.ToLocalDate(until) : null, clock.ToLocalDate(r.CreatedAtUtc))).ToList();

        var penalties = summary.ActivePenalties.Select(p => new PenaltyLine(p.PenaltyId, books[p.BookId], p.Points, p.Reason,
            clock.ToLocalDate(p.ExpiresAtUtc))).ToList();

        return new StaffBorrowingView(staffMemberId, loans, reservations, penalties, summary.ActivePenaltyPoints, rules);
    }

    /// <summary>A book Catalog cannot resolve still gets a line: the loan or penalty exists whatever Catalog says.</summary>
    private static BorrowedBook Describe(Guid bookId, BookDto? book) => book is null
        ? new BorrowedBook(bookId, "Unknown book", null, new BookCoverModel("Unknown book", null, null, bookId, Mini: true), null)
        : new BorrowedBook(bookId, book.Title, book.Authors.Count > 0 ? string.Join(", ", book.Authors) : null,
            new BookCoverModel(book.Title, null, book.CoverImageUrl, book.CategoryId, Mini: true),
            book.IsDeleted ? null : Pages.ReserveModel.BookPath(bookId));
}

/// <summary>The wording shared by the Borrowing pages and blocks.</summary>
public static class Texts
{
    /// <summary>"9 Oct 2026", as on every page of the demo.</summary>
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"1 working day", "6 working days".</summary>
    public static string Count(int number, string noun) => number == 1 ? $"1 {noun}" : $"{number} {noun}s";

    /// <summary>“Atomic Habits”: titles in the messages of the desk.</summary>
    public static string Quoted(string title) => $"“{title}”";

    public static string Ordinal(int number) => ReservePanelModel.Ordinal(number);
}
