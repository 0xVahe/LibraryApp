namespace LibraryApp.Application.Catalog;

/// <summary>One language of the corporate collection: its ISO 639-1 code (as stored on a book) and its English name.</summary>
public sealed record CatalogLanguage(string Code, string Name);

/// <summary>
/// The languages the Catalog offers: Armenian, English and Russian (CAT-05 language filter, the book form, the CSV
/// import). One list, so every page shows the same choices. Books store the code (for example "en"); pages show the name.
/// </summary>
public static class CatalogLanguages
{
    public static readonly IReadOnlyList<CatalogLanguage> All =
    [
        new("hy", "Armenian"),
        new("en", "English"),
        new("ru", "Russian"),
    ];

    /// <summary>True when the code is one of <see cref="All"/>; upper or lower case.</summary>
    public static bool IsKnown(string? code) => Find(code) is not null;

    /// <summary>The English name of the code ("en" → "English"); an unknown code is shown as it is.</summary>
    public static string NameOf(string code) => Find(code)?.Name ?? code;

    private static CatalogLanguage? Find(string? code) =>
        All.FirstOrDefault(language => string.Equals(language.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}
