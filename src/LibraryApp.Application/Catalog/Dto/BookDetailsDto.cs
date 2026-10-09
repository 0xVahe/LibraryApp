namespace LibraryApp.Application.Catalog.Dto;

/// <summary>A book as its details page shows it (CAT-06).</summary>
/// <param name="Id">Book id.</param>
/// <param name="Title">Book title.</param>
/// <param name="Isbn">13 digits, no hyphens.</param>
/// <param name="PublicationYear">Year of publication.</param>
/// <param name="Language">ISO 639-1 code, for example "en".</param>
/// <param name="Description">Optional description.</param>
/// <param name="CategoryId">The one category of the book (BR-CAT-5).</param>
/// <param name="CategoryName">Name of that category.</param>
/// <param name="PublisherId">Publisher id; null when the book has no publisher.</param>
/// <param name="PublisherName">Publisher name; null when the book has no publisher.</param>
/// <param name="Authors">Author names, in alphabetical order.</param>
/// <param name="TotalCopies">Copies the library still has: every status except Lost and Withdrawn.</param>
/// <param name="AvailableCopies">Copies with status Available.</param>
/// <param name="Availability">The two numbers as text, for example "2 of 3 available".</param>
public sealed record BookDetailsDto(
    Guid Id,
    string Title,
    string Isbn,
    int PublicationYear,
    string Language,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    Guid? PublisherId,
    string? PublisherName,
    IReadOnlyList<string> Authors,
    int TotalCopies,
    int AvailableCopies,
    string Availability);
