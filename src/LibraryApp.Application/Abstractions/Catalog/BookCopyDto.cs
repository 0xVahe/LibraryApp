using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Abstractions.Catalog;

/// <summary>
/// One physical copy of a book, as other modules see it. Part of the frozen Catalog contract.
/// </summary>
/// <param name="Id">Copy id. Loan stores it as <c>BookCopyId</c>.</param>
/// <param name="BookId">The book this copy belongs to.</param>
/// <param name="Barcode">Unique across all copies, including retired ones (BR-CAT-2).</param>
/// <param name="Status">Current status of the copy.</param>
/// <param name="Condition">Physical condition of the copy.</param>
/// <param name="ShelfLocation">Where the copy stands in the library; null when not set.</param>
public sealed record BookCopyDto(
    Guid Id,
    Guid BookId,
    string Barcode,
    BookCopyStatus Status,
    BookCopyCondition Condition,
    string? ShelfLocation);
