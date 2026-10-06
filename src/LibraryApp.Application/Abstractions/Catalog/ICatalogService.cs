// TEMPORARY FILE. The real ICatalogService, BookCopyDto and BookDto belong to Group 1.
// When their branch is merged: delete this file and fix the property names in the stub if they differ.
using LibraryApp.Domain.Catalog;   // BookCopyStatus (check the real namespace)

namespace LibraryApp.Application.Abstractions.Catalog;

/// <summary>What the Catalog module offers other modules. Provider: Group 1. Consumer: Borrowing (Group 3).</summary>
public interface ICatalogService
{
    /// <summary>INT-01. Finds a free copy of the book. Never changes any status. Null = no free copy, so a reservation is allowed (BR-BOR-3).</summary>
    Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default);

    /// <summary>
    /// INT-02. Changes the status and saves it. Throws NotFoundException (404) if the copy does not exist,
    /// ConflictException (409) if the change is not allowed or the copy was changed at the same time (RowVersion).
    /// </summary>
    Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default);

    /// <summary>CAT-07. Null if the book does not exist. Soft-deleted books are returned with IsDeleted = true.</summary>
    Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default);

    /// <summary>NEW, agreed (BQ-G1-01). Case and spaces are ignored. Null if no copy has this barcode.</summary>
    Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default);

    /// <summary>PROPOSED, not answered by Group 1 yet (BQ-G1-02). Copies by id, so we can get the book of a copy.</summary>
    Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default);

    /// <summary>PROPOSED, not answered by Group 1 yet (BQ-G1-03). Any copy of the book that has this status.</summary>
    Task<BookCopyDto?> FindCopyAsync(Guid bookId, BookCopyStatus status, CancellationToken ct = default);

    /// <summary>PROPOSED, not answered by Group 1 yet (BQ-G1-04). All copy ids of the book, retired ones too.</summary>
    Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default);
}

// Field names are our guess. Compare them with Group 1's real DTOs.
public sealed record BookCopyDto(Guid Id, Guid BookId, string Barcode, BookCopyStatus Status);
public sealed record BookDto(Guid Id, string Title, string Isbn, bool IsDeleted);