namespace LibraryApp.Application.Catalog.Dto;

/// <summary>Data required to create a book (CAT-01).</summary>
/// <param name="Title">Book title, required and at most 300 characters.</param>
/// <param name="Isbn">ISBN-10 or ISBN-13; it is normalized and stored as ISBN-13.</param>
/// <param name="PublicationYear">Year of publication, not later than the current library year.</param>
/// <param name="Language">Two-letter ISO 639-1 language code, for example <c>en</c>.</param>
/// <param name="Description">Optional book description.</param>
/// <param name="CategoryId">Existing category assigned to the book.</param>
/// <param name="PublisherId">Optional existing publisher.</param>
/// <param name="AuthorIds">One or more existing authors for the book.</param>
public sealed record CreateBookRequest(
    string Title,
    string Isbn,
    int PublicationYear,
    string Language,
    string? Description,
    Guid CategoryId,
    Guid? PublisherId,
    IReadOnlyList<Guid> AuthorIds);
