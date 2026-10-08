using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Dto;

/// <summary>A new physical copy of a book (CAT-04). A new copy always starts as Available.</summary>
/// <param name="Barcode">Unique across all copies, retired ones too (BR-CAT-2). Letters, digits and '-', up to 32 characters; stored in upper case.</param>
/// <param name="Condition">Physical condition: New, Good, Fair or Poor.</param>
/// <param name="ShelfLocation">Optional, up to 50 characters, for example "A-03-2".</param>
public sealed record AddCopyRequest(string Barcode, BookCopyCondition Condition, string? ShelfLocation);
