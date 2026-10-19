namespace LibraryApp.Api.Areas.Catalog;

/// <summary>
/// Model of <c>_BookCover</c>: the coloured text cover (title and author), and over it the stored cover image when the
/// book has one. The colour follows the category, so books of one category look alike. When the image cannot load
/// (covers off, offline), the text cover underneath stays visible; no JavaScript is needed.
/// </summary>
public sealed record BookCoverModel(string Title, string? Author, string? ImageUrl, Guid CategoryId, bool Mini = false)
{
    private static readonly string[] Colours = ["", " c2", " c3", " c4", " c5", " c6"];

    public string CssClass =>
        "cover"
        + Colours[(CategoryId.GetHashCode() & int.MaxValue) % Colours.Length]
        + (Mini ? " mini" : "")
        + (ImageUrl is null ? "" : " has-image");
}
