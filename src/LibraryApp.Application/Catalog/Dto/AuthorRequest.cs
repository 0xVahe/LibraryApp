namespace LibraryApp.Application.Catalog.Dto;

/// <summary>Creates or renames an author (CAT-02).</summary>
/// <param name="FullName">Required, up to 200 characters.</param>
public sealed record AuthorRequest(string? FullName);
