using LibraryApp.Api.Ui.Shared;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Api.Areas.Catalog;

/// <summary>The badge of a book suggestion (CAT-10): Pending grey, Approved green, Rejected red.</summary>
public static class SuggestionStatusBadge
{
    public static BadgeModel For(BookSuggestionStatus status) => status switch
    {
        BookSuggestionStatus.Pending => StatusBadge.Custom("Pending", "grey"),
        BookSuggestionStatus.Approved => StatusBadge.Custom("Approved", "green"),
        BookSuggestionStatus.Rejected => StatusBadge.Custom("Rejected", "red"),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The suggestion status has no badge."),
    };
}
