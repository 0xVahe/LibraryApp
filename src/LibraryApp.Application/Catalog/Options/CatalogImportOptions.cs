using System.Globalization;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Catalog.Options;

/// <summary>
/// Limits of the book CSV import (CAT-09), section <c>Catalog:Import</c>. Both values are required in configuration,
/// are checked at start and take effect after a restart (README, "Business-rule settings").
/// </summary>
public sealed class CatalogImportOptions
{
    public const string SectionName = "Catalog:Import";

    /// <summary>The most book rows one file may hold, the header line not counted.</summary>
    public int MaxRows { get; set; }

    /// <summary>The largest file, in bytes. The parser reads the whole file into memory.</summary>
    public long MaxFileBytes { get; set; }

    /// <summary>The file limit as the librarian reads it, in decimal units: 1000000 is "1 MB", 500000 is "500 KB".</summary>
    public string MaxFileSize
    {
        get
        {
            var (value, unit) = MaxFileBytes switch
            {
                >= 1_000_000 => (MaxFileBytes / 1_000_000m, "MB"),
                >= 1_000 => (MaxFileBytes / 1_000m, "KB"),
                _ => ((decimal)MaxFileBytes, "bytes"),
            };
            return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + unit;
        }
    }
}

/// <summary>Checks <c>Catalog:Import</c> at start: a missing or invalid value stops the API with the name of the key.</summary>
public sealed class CatalogImportOptionsValidator : IValidateOptions<CatalogImportOptions>
{
    public ValidateOptionsResult Validate(string? name, CatalogImportOptions options)
    {
        var failures = new List<string>();

        if (options.MaxRows <= 0)
            failures.Add($"{CatalogImportOptions.SectionName}:MaxRows is required and must be greater than zero.");

        // The parser reads the file into one string, so the limit stays within Int32.
        if (options.MaxFileBytes is <= 0 or > int.MaxValue)
            failures.Add($"{CatalogImportOptions.SectionName}:MaxFileBytes is required and must be between 1 and {int.MaxValue}.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
