namespace LibraryApp.Application.Catalog.Dto;

/// <summary>One book in the search result (CAT-05).</summary>
/// <param name="TotalCopies">Copies in the collection: Lost and Withdrawn copies are not counted (as in BookDto, CAT-06).</param>
/// <param name="CoverImageUrl">Full public cover URL, or null when the book has no cover.</param>
public sealed record BookSearchResultDto(
    Guid Id,
    string Title,
    string Isbn,
    int PublicationYear,
    IReadOnlyList<string> Authors,
    string Category,
    string Language,
    int AvailableCopies,
    int TotalCopies,
    string? CoverImageUrl = null);
