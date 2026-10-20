using LibraryApp.Application.Reporting.Options;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class ReportingOptionsValidatorTests
{
    [Fact]
    public void Accepts_valid_options()
    {
        var options = new ReportingOptions();

        var result = new ReportingOptionsValidator()
            .Validate(null, options);

        Assert.False(result.Failed);
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(-1, 10, 5)]
    [InlineData(5, 0, 5)]
    [InlineData(5, 10, -1)]
    public void Rejects_invalid_options(
        int cacheMinutes,
        int booksLimit,
        int categoriesLimit)
    {
        var options = new ReportingOptions
        {
            DashboardCacheMinutes = cacheMinutes,
            TopBooksLimit = booksLimit,
            TopCategoriesLimit = categoriesLimit
        };

        var result = new ReportingOptionsValidator()
            .Validate(null, options);

        Assert.True(result.Failed);
    }
}
