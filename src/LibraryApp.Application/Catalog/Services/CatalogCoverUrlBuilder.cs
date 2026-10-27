using LibraryApp.Application.Catalog.Options;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>Stored cover path + <c>Catalog:Covers:BaseUrl</c> = the public cover URL; null when covers are off.</summary>
public sealed class CatalogCoverUrlBuilder(IOptions<CatalogCoversOptions> options) : ICatalogCoverUrlBuilder
{
    private readonly string _baseUrl = options.Value.BaseUrl;

    public string? Build(string? coverImagePath)
    {
        if (string.IsNullOrWhiteSpace(coverImagePath) || string.IsNullOrEmpty(_baseUrl))
            return null;

        return _baseUrl + coverImagePath.TrimStart('/');
    }
}
