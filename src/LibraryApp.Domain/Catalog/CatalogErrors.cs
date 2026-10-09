using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

/// <summary>
/// Errors of the Catalog module (SD-04). The code never changes; the message may be reworded.
/// </summary>
public static class CatalogErrors
{
    // ---------- Book ----------

    public static readonly Error BookIsbnDuplicate =
        Error.Conflict(
            "catalog.book_isbn_duplicate",
            "A book with this ISBN already exists.");

    public static readonly Error BookHasCopiesInCirculation =
        Error.Conflict(
            "catalog.book_has_copies_in_circulation",
            "A book cannot be deleted while copies are on loan or reserved.");

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

    public static readonly Error PublisherNotFound =
        Error.NotFound("catalog.publisher_not_found", "The publisher does not exist.");
    
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

    // ---------- Book lookups (used by copies; books use them too) ----------

    public static readonly Error BookNotFound =
        Error.NotFound("catalog.book_not_found", "The book does not exist.");

    public static readonly Error BookDeleted =
        Error.Conflict("catalog.book_deleted", "The book is deleted, so no copies can be added to it.");

    // ---------- BookCopy management (CAT-04) ----------

    public static readonly Error CopyNotFound =
        Error.NotFound("catalog.copy_not_found", "The copy does not exist or belongs to another book.");

    public static readonly Error CopyBarcodeDuplicate =
        Error.Conflict("catalog.copy_barcode_duplicate",
            "A copy with this barcode already exists, retired copies included (BR-CAT-2).");

    /// <summary>The copy is OnLoan or Reserved: Borrowing changes its status (return, declare lost), not the librarian here.</summary>
    public static readonly Error CopyInCirculation =
        Error.Conflict("catalog.copy_in_circulation",
            "The copy is on loan or reserved, so its status is changed through Borrowing (return or declare lost).");

    /// <summary>BR-CAT-4: a librarian sets only UnderRepair, Lost and Withdrawn, and Available after a repair.</summary>
    public static Error CopyStatusNotAllowedForLibrarian(BookCopyStatus status) =>
        Error.BusinessRule("catalog.copy_status_not_allowed",
            $"A librarian cannot set the status {status} here (BR-CAT-4).");

    // ---------- Author management (CAT-02) ----------

    public static readonly Error AuthorNotFound =
        Error.NotFound("catalog.author_not_found", "The author does not exist.");

    /// <summary>The author is linked to a book, a deleted book included: its row stays and the foreign key keeps it.</summary>
    public static readonly Error AuthorInUse =
        Error.Conflict("catalog.author_in_use", "An author linked to a book cannot be deleted.");

    public static readonly Error BookAuthorsRequired =
        Error.Validation("catalog.book_authors_required", "A book must have at least one author (BR-CAT-5).");

    // ---------- Category management (CAT-03) ----------

    public static readonly Error CategoryNotFound =
        Error.NotFound("catalog.category_not_found", "The category does not exist.");

    public static readonly Error CategoryInUse =
        Error.Conflict("catalog.category_in_use", "A category referenced by books or subcategories cannot be deleted.");
}