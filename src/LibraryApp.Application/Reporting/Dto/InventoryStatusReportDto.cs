using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Reporting.Dto;

/// <summary>
/// REP-05: copies by status, each split by condition. Every status and condition is listed, also with 0 copies.
/// </summary>
/// <param name="AsOfDate">The day of the report (Asia/Yerevan): the copies as they are now.</param>
/// <param name="GeneratedAtUtc">When the report was made.</param>
/// <param name="CategoryId">The category filter, or null for the whole library. A category includes its subcategories, like
/// the catalog search.</param>
/// <param name="Category">The name of the category filter, or null for the whole library.</param>
/// <param name="TotalCopies">Every copy, Lost and Withdrawn ones included (also the copies of deleted books).</param>
/// <param name="CopiesInCollection">The copies the library still has: every copy except Lost and Withdrawn, as the total
/// copies of the book details (CAT-06).</param>
/// <param name="Statuses">Every copy status, in the order of the status list, each with its copies per condition.</param>
public sealed record InventoryStatusReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    Guid? CategoryId,
    string? Category,
    int TotalCopies,
    int CopiesInCollection,
    IReadOnlyList<InventoryStatusItemDto> Statuses);

/// <summary>The copies of one status (REP-05).</summary>
/// <param name="Status">The copy status.</param>
/// <param name="Copies">The copies with this status, in every condition.</param>
/// <param name="Conditions">Every condition, in the order of the condition list, with its copies of this status.</param>
public sealed record InventoryStatusItemDto(
    BookCopyStatus Status,
    int Copies,
    IReadOnlyList<InventoryConditionItemDto> Conditions);

/// <summary>The copies of one status in one condition (REP-05).</summary>
/// <param name="Condition">The physical condition.</param>
/// <param name="Copies">The copies of the status in this condition.</param>
public sealed record InventoryConditionItemDto(BookCopyCondition Condition, int Copies);
