using System.Text.RegularExpressions;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public static partial class CoverImagePathRules
{
    public const string Message =
        "Cover image must be a file in the library’s cover storage.";

    public static bool IsValid(string? value, bool coversEnabled = true)
    {
        if (value is null || value.Length == 0)
            return true;

        if (!coversEnabled)
            return false;

        return value.Length <= Book.CoverImagePathMaxLength
            && !value.StartsWith("/", StringComparison.Ordinal)
            && !value.Contains("..", StringComparison.Ordinal)
            && !value.Contains('\\')
            && PathPattern().IsMatch(value);
    }

    [GeneratedRegex("^[A-Za-z0-9/_-]+\\.(png|jpg|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
