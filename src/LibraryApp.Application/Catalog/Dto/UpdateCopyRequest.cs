using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Dto;

/// <summary>Changes the details of a copy (CAT-04). The barcode cannot be changed; the status has its own request.</summary>
/// <param name="Condition">Physical condition: New, Good, Fair or Poor.</param>
/// <param name="ShelfLocation">Optional, up to 50 characters; empty removes it.</param>
public sealed record UpdateCopyRequest(BookCopyCondition Condition, string? ShelfLocation);
