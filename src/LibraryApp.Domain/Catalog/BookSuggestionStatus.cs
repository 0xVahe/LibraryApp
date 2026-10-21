namespace LibraryApp.Domain.Catalog;

/// <summary>Status of a book suggestion (CAT-10). Stored as a number: never renumber a value.</summary>
public enum BookSuggestionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}
