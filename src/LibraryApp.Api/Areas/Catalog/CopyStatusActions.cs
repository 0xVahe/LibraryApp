using LibraryApp.Domain.Catalog;

namespace LibraryApp.Api.Areas.Catalog;

/// <summary>A status button of the copies panel: the status it sets and its text.</summary>
public sealed record CopyStatusAction(BookCopyStatus Status, string Label);

/// <summary>
/// The status changes a librarian may make from the copies panel (CAT-04, BR-CAT-4): the ones the copy service
/// accepts, so the panel never shows a button that would be refused. On loan and Reserved copies belong to a loan or
/// a reservation (only Borrowing changes them); Lost and Withdrawn are final.
/// </summary>
public static class CopyStatusActions
{
    private static readonly CopyStatusAction ToRepair = new(BookCopyStatus.UnderRepair, "Send to repair");
    private static readonly CopyStatusAction Repaired = new(BookCopyStatus.Available, "Back on the shelf");
    private static readonly CopyStatusAction MarkLost = new(BookCopyStatus.Lost, "Mark lost");
    private static readonly CopyStatusAction Withdraw = new(BookCopyStatus.Withdrawn, "Withdraw");

    public static IReadOnlyList<CopyStatusAction> For(BookCopyStatus current) => current switch
    {
        BookCopyStatus.Available => [ToRepair, MarkLost, Withdraw],
        BookCopyStatus.UnderRepair => [Repaired, MarkLost, Withdraw],
        _ => [],
    };

    /// <summary>Lost and Withdrawn copies have left the collection: their shelf and condition are no longer edited.</summary>
    public static bool CanEditDetails(BookCopyStatus current) =>
        current is not (BookCopyStatus.Lost or BookCopyStatus.Withdrawn);

    /// <summary>Why a copy has no status buttons, shown in their place.</summary>
    public static string? NoActionsReason(BookCopyStatus current) => current switch
    {
        BookCopyStatus.OnLoan or BookCopyStatus.Reserved => "Changed by the loan desk",
        BookCopyStatus.Lost or BookCopyStatus.Withdrawn => "Final",
        _ => null,
    };
}
