using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

/// <summary>
/// A title in the catalog (one ISBN). Deleting is soft (BR-CAT-3): the row stays, so loan history keeps working.
/// The ISBN format (13 digits, check digit) and the publication year range are checked by the validator,
/// because the year depends on the clock.
/// </summary>
public sealed class Book : Entity
{
    public const int TitleMaxLength = 300;
    public const int IsbnLength = 13;
    public const int LanguageLength = 2;
    public const int CoverImagePathMaxLength = 300;

    private Book() { }                                           // for EF Core only

    public Book(
        Guid id,
        string title,
        string isbn,
        int publicationYear,
        string language,
        string? description,
        Guid categoryId,
        Guid? publisherId,
        string? coverImagePath = null) : base(id)
    {
        Update(title, isbn, publicationYear, language, description, categoryId, publisherId, coverImagePath);
    }

    public string Title { get; private set; } = null!;

    /// <summary>13 digits without hyphens or spaces (G1-02). Unique among books that are not deleted.</summary>
    public string Isbn { get; private set; } = null!;

    public int PublicationYear { get; private set; }

    /// <summary>ISO 639-1 code, two lower case letters (G1-03).</summary>
    public string Language { get; private set; } = null!;

    public string? Description { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid? PublisherId { get; private set; }

    public string? CoverImagePath { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public void Update(
        string title,
        string isbn,
        int publicationYear,
        string language,
        string? description,
        Guid categoryId,
        Guid? publisherId,
        string? coverImagePath = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException(CatalogErrors.BookTitleRequired);

        if (string.IsNullOrWhiteSpace(isbn))
            throw new DomainException(CatalogErrors.BookIsbnRequired);

        if (string.IsNullOrWhiteSpace(language))
            throw new DomainException(CatalogErrors.BookLanguageRequired);

        if (categoryId == Guid.Empty)
            throw new DomainException(CatalogErrors.BookCategoryRequired);

        Title = title.Trim();
        Isbn = isbn.Trim();
        PublicationYear = publicationYear;
        Language = language.Trim().ToLowerInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CategoryId = categoryId;
        PublisherId = publisherId;
        CoverImagePath = string.IsNullOrWhiteSpace(coverImagePath) ? null : coverImagePath.Trim();
    }

    /// <summary>Soft delete. The check "no copy is OnLoan or Reserved" (BR-CAT-3) is made by the service before this call.</summary>
    public void Delete(DateTime deletedAtUtc)
    {
        if (IsDeleted)
            throw new DomainException(CatalogErrors.BookAlreadyDeleted);

        IsDeleted = true;
        DeletedAtUtc = deletedAtUtc;
    }
}
