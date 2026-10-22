using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Reporting.Dto;

/// <summary>
/// REP-05: copies by status, each split by condition, as of <see cref="AsOfDate"/>. Every status and condition is
/// listed, also with 0 copies. <see cref="CategoryId"/> null: the whole library; otherwise the category and its
/// subcategories, like the catalog search.
/// </summary>
/// <param name="TotalCopies">Every copy, Lost and Withdrawn ones included (also the copies of deleted books).</param>
/// <param name="CopiesInCollection">The copies the library still has: every copy except Lost and Withdrawn, as the
/// total copies of the book details (CAT-06).</param>
public sealed record InventoryStatusReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    Guid? CategoryId,
    string? Category,
    int TotalCopies,
    int CopiesInCollection,
    IReadOnlyList<InventoryStatusItemDto> Statuses);

public sealed record InventoryStatusItemDto(
    BookCopyStatus Status,
    int Copies,
    IReadOnlyList<InventoryConditionItemDto> Conditions);

public sealed record InventoryConditionItemDto(BookCopyCondition Condition, int Copies);
