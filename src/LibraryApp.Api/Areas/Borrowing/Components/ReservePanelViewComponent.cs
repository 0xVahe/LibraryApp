using System.Globalization;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Borrowing;
using Microsoft.AspNetCore.Mvc;

namespace LibraryApp.Api.Areas.Borrowing.Components;

/// <summary>
/// "Get this book" on Group 1's Book page (UI guide Part 1 "Group 3", demo <c>book.html</c>; contract in the integration
/// answers, section 5): <c>@await Component.InvokeAsync("ReservePanel", new { bookId = Model.Book.Id })</c>.
/// The signed-in member comes from <see cref="ICurrentUser"/>. No copy count and no next return date: Group 1's
/// <c>BookDetails</c> shows them. The reservation itself is posted to <see cref="Pages.ReserveModel"/>.
/// </summary>
public sealed class ReservePanelViewComponent(
    ICatalogService catalog,
    IStaffMemberService staffMembers,
    IBorrowingReadService borrowing,
    ICurrentUser currentUser,
    IClock clock) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid bookId)
    {
        CancellationToken ct = HttpContext.RequestAborted;
        bool onShelf = await catalog.GetAvailableCopyAsync(bookId, ct) is not null;

        // An account without a staff profile can look, not reserve.
        if (currentUser.StaffMemberId is not { } member)
            return View("~/Areas/Borrowing/Components/ReservePanel/Default.cshtml", new ReservePanelModel(bookId, onShelf ? ReservePanelState.OnShelf : ReservePanelState.AllOut));

        StaffBorrowingSummary summary = await borrowing.GetStaffSummaryAsync(member, ct);
        LendingRules rules = await staffMembers.GetLendingRulesAsync(member, ct);
        OpenReservationInfo? reservation = summary.Reservations.FirstOrDefault(r => r.BookId == bookId);

        ReservePanelState state =
            summary.ActiveLoans.Any(loan => loan.BookId == bookId) ? ReservePanelState.OnLoanToYou
            : reservation is not null ? ReservePanelState.Reserved
            : onShelf ? ReservePanelState.OnShelf
            : ReservePanelState.AllOut;

        return View("~/Areas/Borrowing/Components/ReservePanel/Default.cshtml", new ReservePanelModel(bookId, state)
        {
            CanReserve = state == ReservePanelState.AllOut,
            Reservation = state == ReservePanelState.Reserved ? reservation : null,
            ReadyUntilLocal = reservation?.ReadyUntilUtc is { } until ? clock.ToLocalDate(until) : null,
            OpenLoans = summary.ActiveLoans.Count,
            Rules = rules,
        });
    }
}

/// <summary>What the panel says, first match wins: the member has the book, has reserved it, a copy is on the shelf, all out.</summary>
public enum ReservePanelState
{
    OnLoanToYou = 1,
    Reserved = 2,
    OnShelf = 3,
    AllOut = 4,
}

public sealed record ReservePanelModel(Guid BookId, ReservePanelState State)
{
    /// <summary>The "Reserve a copy" form: only when every copy is out (BR-BOR-3) and a staff member is signed in.</summary>
    public bool CanReserve { get; init; }

    public OpenReservationInfo? Reservation { get; init; }

    public DateOnly? ReadyUntilLocal { get; init; }

    public int OpenLoans { get; init; }

    /// <summary>The member's loan limits; null for an account without a staff profile.</summary>
    public LendingRules? Rules { get; init; }

    public string ReservationText => Reservation switch
    {
        { Status: ReservationStatus.ReadyForPickup } =>
            ReadyUntilLocal is { } until
                ? $"Your copy is waiting at the library desk. Pick it up before {until.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."
                : "Your copy is waiting at the library desk.",
        { } waiting =>
            $"You reserved this book and are {Ordinal(waiting.QueuePosition)} in the queue. You get an email when your copy is ready.",
        null => "",
    };

    public string LimitsText => Rules is { } rules
        ? $"You have {OpenLoans} of {rules.MaxLoans} books on loan. Loan period: {rules.LoanDays} days, " +
          (rules.MaxRenewals switch { 0 => "no renewals.", 1 => "up to 1 renewal.", var n => $"up to {n} renewals." })
        : "";

    /// <summary>1st, 2nd, 3rd, 4th … 11th, 12th, 13th, 21st (demo <c>my-library.html</c>: "you are 2nd in the queue").</summary>
    public static string Ordinal(int number) => (number % 100) switch
    {
        11 or 12 or 13 => $"{number}th",
        _ => (number % 10) switch { 1 => $"{number}st", 2 => $"{number}nd", 3 => $"{number}rd", _ => $"{number}th" },
    };
}
