namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>
/// One seed book: its number gives the fixed IDs of the book and its copies (CatalogSeedIds). CategoryNumber is a category or
/// a subcategory of <see cref="CatalogSeedData.Categories"/>.
/// </summary>
public sealed record SeedBook(
    int Number,
    string Title,
    IReadOnlyList<string> Authors,
    string? Publisher,
    int PublicationYear,
    string Language,
    string Isbn,
    int CategoryNumber);
