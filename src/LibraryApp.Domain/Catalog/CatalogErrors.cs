using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

/// <summary>
/// Errors of the Catalog module (SD-04). The code never changes; the message may be reworded.
/// </summary>
public static class CatalogErrors
{
    // ---------- Book ----------

    public static readonly Error BookTitleRequired =
        Error.Validation("catalog.book_title_required", "Book title is required.");

    public static readonly Error BookIsbnRequired =
        Error.Validation("catalog.book_isbn_required", "Book ISBN is required.");

    public static readonly Error BookLanguageRequired =
        Error.Validation("catalog.book_language_required", "Book language is required.");

    public static readonly Error BookCategoryRequired =
        Error.Validation("catalog.book_category_required", "A book must have a category (BR-CAT-5).");

    public static readonly Error BookAlreadyDeleted =
        Error.Conflict("catalog.book_already_deleted", "The book is already deleted.");

    // ---------- Author, Publisher ----------

    public static readonly Error AuthorNameRequired =
        Error.Validation("catalog.author_name_required", "Author name is required.");

    public static readonly Error PublisherNameRequired =
        Error.Validation("catalog.publisher_name_required", "Publisher name is required.");

    // ---------- Category (two levels at most, CAT-03) ----------

    public static readonly Error CategoryNameRequired =
        Error.Validation("catalog.category_name_required", "Category name is required.");

    public static readonly Error CategoryParentIsItself =
        Error.BusinessRule("catalog.category_parent_is_itself", "A category cannot be its own parent.");

    public static readonly Error CategoryParentIsNotTopLevel =
        Error.BusinessRule("catalog.category_parent_not_top_level",
            "A category can only be placed under a top-level category (two levels at most).");

    public static readonly Error CategoryHasSubcategories =
        Error.BusinessRule("catalog.category_has_subcategories",
            "A category that has subcategories cannot be placed under another category.");

    // ---------- BookCopy ----------

    public static readonly Error CopyBarcodeRequired =
        Error.Validation("catalog.copy_barcode_required", "Copy barcode is required.");

    public static readonly Error CopyConditionUnknown =
        Error.Validation("catalog.copy_condition_unknown", "Unknown copy condition.");

    /// <summary>The code is fixed; the message names the two statuses to help the caller.</summary>
    public static Error CopyStatusChangeNotAllowed(BookCopyStatus from, BookCopyStatus to) =>
        Error.Conflict("catalog.copy_status_change_not_allowed", $"A copy cannot change status from {from} to {to}.");
}
