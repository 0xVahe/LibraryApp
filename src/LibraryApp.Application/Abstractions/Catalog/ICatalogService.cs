using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Abstractions.Catalog;

/// <summary>
/// What the Catalog module offers other modules (INT-01, INT-02, CAT-07). Provider: Group 1. Consumer: Borrowing (Group 3).
/// <para>
/// Every method except <see cref="SetCopyStatusAsync"/> only reads. Only Borrowing sets OnLoan and Reserved
/// (BR-CAT-4); librarians change copies through Catalog's own endpoints, not through this interface.
/// </para>
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// INT-01. One Available copy of the book, or null when the book has no Available copy, is soft-deleted or is
    /// unknown (call <see cref="GetBookAsync"/> to tell these apart). Read only: the copy is not held for the caller.
    /// </summary>
    Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default);

    /// <summary>
    /// INT-02. Changes the status of a copy; the copy status transition rules of Catalog apply.
    /// Only changes data: no SaveChanges, no own transaction, no events. Call it inside the caller's
    /// <c>IUnitOfWork</c> (BR-BOR-4). Two parallel changes of the same copy end in 409 (RowVersion).
    /// Throws <c>catalog.copy_not_found</c> and <c>catalog.copy_status_change_not_allowed</c>.
    /// </summary>
    Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default);

    /// <summary>
    /// CAT-07. The book with its copy counts, or null when the id is unknown. A soft-deleted book is returned with
    /// <see cref="BookDto.IsDeleted"/> = true, so old loans and reservations keep resolving.
    /// </summary>
    Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default);

    /// <summary>
    /// BOR-01. The copy with this barcode, or null when there is none. The barcode is compared trimmed and ignoring
    /// case. Lost and Withdrawn copies are returned too, so the caller can give a clear reason instead of "not found".
    /// </summary>
    Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// The copies with these ids, each once, with their BookId (BOR-03, BOR-07, BR-BOR-1, BR-BOR-2).
    /// Unknown ids are left out of the result. One call for many ids, so a list of loans needs one call, not one per loan.
    /// </summary>
    Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default);

    /// <summary>
    /// BOR-10. Ids of ALL copies of the book, including Lost and Withdrawn ones, so loan history stays complete.
    /// Works for soft-deleted books too. Empty when the book is unknown or has no copies.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default);
}
