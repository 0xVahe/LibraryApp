namespace LibraryApp.Application.Abstractions.Catalog;

/// <summary>Builds the public cover URL from a stored relative path.</summary>
public interface ICatalogCoverUrlBuilder
{
    string? Build(string? coverImagePath);
}
