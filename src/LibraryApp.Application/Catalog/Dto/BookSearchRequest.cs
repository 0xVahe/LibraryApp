using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Dto;

/// <summary>Book search as the client sends it (CAT-05). The service normalizes it into a <see cref="BookSearchFilter"/>.</summary>
/// <param name="Search">Part of the title, an author name, or an ISBN-10, ISBN-13 or part of one (hyphens and spaces allowed).</param>
/// <param name="CategoryId">A category; a top-level category also matches the books of its subcategories.</param>
/// <param name="Language">ISO 639-1 code, for example "en".</param>
/// <param name="Available">true: at least one Available copy; false: none.</param>
/// <param name="Paging">Page number and size (default 20, at most 100).</param>
public sealed record BookSearchRequest(
    string? Search,
    Guid? CategoryId,
    string? Language,
    bool? Available,
    PageRequest Paging);
