namespace LibraryApp.Application.Catalog.Dto;

public sealed record BookSearchResultDto(
    Guid Id,
    string Title,
    IReadOnlyList<string> Authors,
    string Category,
    string Language,
    int AvailableCopies,
    int TotalCopies);
