using System.Text.Json;
using LibraryApp.Application.Staff.Options;

namespace LibraryApp.Application.Tests.Staff;

public sealed class StaffOptionsValidatorTests
{
    [Fact]
    public void The_values_of_appsettings_are_valid()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var options = Assert.IsType<StaffOptions>(settings.RootElement.GetProperty(StaffOptions.SectionName)
            .Deserialize<StaffOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

        var result = new StaffOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    private static StaffOptions Valid() => new()
    {
        Import = new() { MaxRows = 3, MaxFileBytes = 4096 },
        RestrictedLimits = new() { MaxLoans = 2, MaxRenewals = 0 }
    };

    [Fact]
    public void Explicit_zero_renewals_is_valid() =>
        Assert.True(new StaffOptionsValidator().Validate(null, Valid()).Succeeded);

    [Fact]
    public void Missing_settings_name_every_required_key()
    {
        var result = new StaffOptionsValidator().Validate(null, new StaffOptions());

        Assert.True(result.Failed);
        foreach (var key in new[] { "Import:MaxRows", "Import:MaxFileBytes", "RestrictedLimits:MaxLoans", "RestrictedLimits:MaxRenewals" })
            Assert.Contains(result.Failures, failure => failure.Contains("Staff:" + key, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 4096L, 1, 0, "Import:MaxRows")]
    [InlineData(-1, 4096L, 1, 0, "Import:MaxRows")]
    [InlineData(3, 0L, 1, 0, "Import:MaxFileBytes")]
    [InlineData(3, -1L, 1, 0, "Import:MaxFileBytes")]
    [InlineData(3, 2147483648L, 1, 0, "Import:MaxFileBytes")]
    [InlineData(3, 4096L, 0, 0, "RestrictedLimits:MaxLoans")]
    [InlineData(3, 4096L, -1, 0, "RestrictedLimits:MaxLoans")]
    [InlineData(3, 4096L, 1, -1, "RestrictedLimits:MaxRenewals")]
    [InlineData(3, 4096L, 1, 1, "RestrictedLimits:MaxRenewals")]
    [InlineData(3, 4096L, 1, 2, "RestrictedLimits:MaxRenewals")]
    [InlineData(3, 4096L, 1, null, "RestrictedLimits:MaxRenewals")]
    public void Invalid_or_missing_values_fail(int rows, long bytes, int loans, int? renewals, string key)
    {
        var options = new StaffOptions
        {
            Import = new() { MaxRows = rows, MaxFileBytes = bytes },
            RestrictedLimits = new() { MaxLoans = loans, MaxRenewals = renewals }
        };

        var result = new StaffOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Staff:" + key, StringComparison.Ordinal));
    }

    [Fact]
    public void Null_sections_report_clear_errors()
    {
        var result = new StaffOptionsValidator().Validate(null, new StaffOptions
        {
            Import = null!, RestrictedLimits = null!
        });

        Assert.NotNull(result.Failures);
        Assert.Contains("Staff:Import is required.", result.Failures);
        Assert.Contains("Staff:RestrictedLimits is required.", result.Failures);
    }
}
