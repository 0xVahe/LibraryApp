using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Api.Areas.Catalog.Pages.Books;

/// <summary>
/// What the book form posts (CAT-01). Every value is nullable so an empty field reaches the validator of the request
/// (<c>CreateBookRequestValidator</c>, <c>UpdateBookRequestValidator</c>) instead of a binding error.
/// </summary>
public sealed class BookFormInput
{
    public string? Title { get; set; }

    public string? Isbn { get; set; }

    public int? PublicationYear { get; set; }

    public string? Language { get; set; }

    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Null for "No publisher".</summary>
    public Guid? PublisherId { get; set; }

    /// <summary>Only the New book form sends authors; Edit book changes them in the authors picker (CAT-02).</summary>
    public List<Guid> AuthorIds { get; set; } = [];

    /// <summary>The form of an existing book.</summary>
    public static BookFormInput From(BookDetailsDto book) => new()
    {
        Title = book.Title,
        Isbn = book.Isbn,
        PublicationYear = book.PublicationYear,
        Language = book.Language,
        Description = book.Description,
        CategoryId = book.CategoryId,
        PublisherId = book.PublisherId,
    };

    public CreateBookRequest ToCreateRequest() => new(
        Title?.Trim() ?? "",
        Isbn?.Trim() ?? "",
        PublicationYear ?? 0,
        Language?.Trim() ?? "",
        DescriptionOrNull(),
        CategoryId ?? Guid.Empty,
        PublisherId,
        AuthorIds);

    public UpdateBookRequest ToUpdateRequest() => new(
        Title?.Trim() ?? "",
        Isbn?.Trim() ?? "",
        PublicationYear ?? 0,
        Language?.Trim() ?? "",
        DescriptionOrNull(),
        CategoryId ?? Guid.Empty,
        PublisherId);

    private string? DescriptionOrNull() => string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
}
