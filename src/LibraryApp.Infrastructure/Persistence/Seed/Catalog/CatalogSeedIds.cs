namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>
/// Stable identifiers of the catalog seed: categories, books and copies (D3). Authors and publishers keep the IDs listed in
/// <see cref="CatalogSeedData"/>. Other seeds (loans, reservations) reference these values, so they never change.
/// </summary>
public static class CatalogSeedIds
{
    /// <summary>Seeded category <paramref name="number"/>: 10000000-…-0001-{number}, in the order of <see cref="CatalogSeedData.Categories"/>.</summary>
    public static Guid Category(int number) =>
        new($"10000000-0000-0000-0001-{number:D12}");

    /// <summary>Seeded book <paramref name="number"/>: b0000000-…-{number}, the same number as in <see cref="SeedBook.Number"/>.</summary>
    public static Guid Book(int number) =>
        new($"b0000000-0000-0000-0000-{number:D12}");

    /// <summary>Copy <paramref name="copyNumber"/> of seeded book <paramref name="bookNumber"/>: c0000000-…-{book × 1000 + copy}.</summary>
    public static Guid Copy(int bookNumber, int copyNumber) =>
        new($"c0000000-0000-0000-0000-{bookNumber * 1000 + copyNumber:D12}");

    /// <summary>Barcode of that copy, for example BK-0001-01 for the first copy of book 1 (BR-CAT-2).</summary>
    public static string CopyBarcode(int bookNumber, int copyNumber) =>
        $"BK-{bookNumber:D4}-{copyNumber:D2}";

    /// <summary>Stored cover path of that book, for example library-covers/01.png; the URL is built from Catalog:Covers:BaseUrl.</summary>
    public static string CoverImagePath(int bookNumber) =>
        $"library-covers/{bookNumber:D2}.png";
}
