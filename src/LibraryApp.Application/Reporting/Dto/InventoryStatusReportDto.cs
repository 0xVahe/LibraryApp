using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Reporting.Dto;

/// <summary>
/// REP-05: copies by status, each split by condition. Every status and condition is listed, also with 0 copies.
/// <see cref="CategoryId"/> null: the whole library; otherwise the category and its subcategories.
/// </summary>
public sealed record InventoryStatusReportDto(
    Guid? CategoryId,
    string? Category,
    DateTime GeneratedAtUtc,
    int TotalCopies,
    IReadOnlyList<InventoryStatusItemDto> Statuses);

public sealed record InventoryStatusItemDto(
    BookCopyStatus Status,
    int Copies,
    IReadOnlyList<InventoryConditionItemDto> Conditions);

public sealed record InventoryConditionItemDto(BookCopyCondition Condition, int Copies);
