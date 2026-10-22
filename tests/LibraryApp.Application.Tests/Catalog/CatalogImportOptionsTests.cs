using LibraryApp.Application.Catalog.Options;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>CAT-09: the <c>Catalog:Import</c> limits are required and checked at start, like the other module rules.</summary>
public class CatalogImportOptionsTests
{
    [Theory]
    [InlineData(500, 1_000_000, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, int.MaxValue, true)]
    [InlineData(0, 1_000_000, false)]
    [InlineData(-1, 1_000_000, false)]
    [InlineData(500, 0, false)]
    [InlineData(500, (long)int.MaxValue + 1, false)]
    public void Both_limits_are_required_and_in_range(int maxRows, long maxFileBytes, bool valid)
    {
        var result = new CatalogImportOptionsValidator().Validate(null,
            new CatalogImportOptions { MaxRows = maxRows, MaxFileBytes = maxFileBytes });

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void A_missing_section_names_both_keys()
    {
        var result = new CatalogImportOptionsValidator().Validate(null, new CatalogImportOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.StartsWith("Catalog:Import:MaxRows", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.StartsWith("Catalog:Import:MaxFileBytes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1_000_000, "1 MB")]
    [InlineData(1_500_000, "1.5 MB")]
    [InlineData(500_000, "500 KB")]
    [InlineData(999, "999 bytes")]
    public void The_file_limit_is_shown_in_decimal_units(long maxFileBytes, string expected) =>
        Assert.Equal(expected, new CatalogImportOptions { MaxFileBytes = maxFileBytes }.MaxFileSize);
}
