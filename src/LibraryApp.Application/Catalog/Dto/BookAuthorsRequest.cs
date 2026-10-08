namespace LibraryApp.Application.Catalog.Dto;

/// <summary>The full new list of a book's authors (CAT-02). At least one author (BR-CAT-5); duplicates are ignored.</summary>
public sealed record BookAuthorsRequest(IReadOnlyList<Guid>? AuthorIds);
