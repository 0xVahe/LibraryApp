namespace LibraryApp.Application.Catalog.Services;

/// <summary>Builds the public cover URL from a stored relative path.</summary>
public interface ICatalogCoverUrlBuilder
{
    string? Build(string? coverImagePath);
}
