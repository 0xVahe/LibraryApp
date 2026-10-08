namespace LibraryApp.Application.Catalog.Dto;

/// <summary>
/// A normalized search: empty values are null, text is trimmed, the language is lower case and
/// <see cref="IsbnText"/> is the search text without hyphens and spaces (ISBNs are stored as 13 digits).
/// </summary>
public sealed record BookSearchFilter(
    string? Text,
    string? IsbnText,
    Guid? CategoryId,
    string? Language,
    bool? Available);