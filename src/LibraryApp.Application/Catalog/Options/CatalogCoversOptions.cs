using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Catalog.Options;

/// <summary>Optional storage address for catalog cover paths. Empty disables covers; the value is validated at startup.</summary>
public sealed class CatalogCoversOptions
{
    public const string SectionName = "Catalog:Covers";

    public string BaseUrl { get; set; } = string.Empty;
}

public sealed class CatalogCoversOptionsValidator : IValidateOptions<CatalogCoversOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogCoversOptions options)
    {
        if (string.IsNullOrEmpty(options.BaseUrl))
            return ValidateOptionsResult.Success;

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !options.BaseUrl.EndsWith("/", StringComparison.Ordinal)
            || options.BaseUrl.Any(character => character is ' ' or ';' or '\'' or ','))
        {
            return ValidateOptionsResult.Fail(
                $"{CatalogCoversOptions.SectionName}:BaseUrl must be empty or an absolute HTTPS URL ending with '/' without userinfo, query, fragment or unsafe characters.");
        }

        return ValidateOptionsResult.Success;
    }
}
