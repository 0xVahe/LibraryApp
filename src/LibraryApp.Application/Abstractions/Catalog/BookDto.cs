namespace LibraryApp.Application.Abstractions.Catalog;

/// <summary>
/// A book with its copy counts, as other modules see it. Part of the frozen Catalog contract.
/// <para>
/// It has no next expected return date (CAT-08) on purpose: Catalog gets that date from Borrowing, so the date here
/// would make <c>GetBookAsync</c> (called by Borrowing) call Borrowing again. The date belongs to Catalog's own
/// book-details model (CAT-06).
/// </para>
/// </summary>
/// <param name="Id">Book id.</param>
/// <param name="Title">Book title.</param>
/// <param name="Isbn">ISBN-10 or ISBN-13 (BR-CAT-1).</param>
/// <param name="PublicationYear">Year of publication.</param>
/// <param name="Language">Language of the book.</param>
/// <param name="Description">Optional description.</param>
/// <param name="CategoryId">The one category of the book (BR-CAT-5).</param>
/// <param name="CategoryName">Name of that category.</param>
/// <param name="PublisherId">Publisher id; null when the book has no publisher.</param>
/// <param name="PublisherName">Publisher name; null when the book has no publisher.</param>
/// <param name="Authors">Author names; at least one (BR-CAT-5).</param>
/// <param name="TotalCopies">Copies the library still has: every status except Lost and Withdrawn (CAT-06).</param>
/// <param name="AvailableCopies">Copies with status Available (CAT-06).</param>
/// <param name="IsDeleted">True for a soft-deleted book (CAT-01, BR-CAT-3).</param>
public sealed record BookDto(
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
    bool IsDeleted);
