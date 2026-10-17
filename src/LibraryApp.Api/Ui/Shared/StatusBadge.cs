using System.Text.RegularExpressions;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Api.Ui.Shared;

public sealed record BadgeModel(string Text, string CssClass);

/// <summary>
/// Status → badge colour (design.md, "Badges"): green for good states, amber for warnings, red for problems, blue for
/// "in use", grey for neutral ones. Every value of the staff, loan, copy and reservation statuses has a colour; a new
/// status without one fails a test.
/// </summary>
public static partial class StatusBadge
{
    public static BadgeModel For(Enum status) => new(Label(status), "badge " + Colour(status));

    /// <summary>A badge with its own text, for example "Due in 5 days" in amber.</summary>
    public static BadgeModel Custom(string text, string colour) => new(text, "badge badge-" + colour);

    public static string Colour(Enum status) => status switch
    {
        StaffMemberStatus.Active => "badge-green",
        StaffMemberStatus.Restricted => "badge-amber",
        StaffMemberStatus.Suspended or StaffMemberStatus.Deactivated => "badge-red",

        LoanStatus.Active => "badge-blue",
        LoanStatus.Returned => "badge-green",
        LoanStatus.Overdue or LoanStatus.Lost => "badge-red",

        BookCopyStatus.Available => "badge-green",
        BookCopyStatus.OnLoan or BookCopyStatus.Reserved => "badge-blue",
        BookCopyStatus.UnderRepair => "badge-amber",
        BookCopyStatus.Lost => "badge-red",
        BookCopyStatus.Withdrawn => "badge-grey",

        ReservationStatus.Waiting => "badge-grey",
        ReservationStatus.ReadyForPickup or ReservationStatus.Fulfilled => "badge-green",
        ReservationStatus.Cancelled or ReservationStatus.Expired => "badge-grey",

        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The status has no badge colour; add it to StatusBadge."),
    };

    /// <summary>"ReadyForPickup" → "Ready for pickup"; "OnLoan" → "On loan". An open loan reads "On loan" (design.md).</summary>
    public static string Label(Enum status)
    {
        if (status is LoanStatus.Active)
            return "On loan";

        var words = WordStart().Replace(status.ToString(), " $1").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex WordStart();
}
