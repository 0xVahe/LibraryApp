using LibraryApp.Application.Reporting.Options;

namespace LibraryApp.Application.Tests.Reporting;

public sealed class ReportingOptionsValidatorTests
{
    [Theory]
    [InlineData(5, 10, 5)]
    [InlineData(1, 1, 1)]
    [InlineData(60, 100, 100)]
    public void Accepts_valid_options(
        int cacheMinutes,
        int booksLimit,
        int categoriesLimit)
    {
        var options = new ReportingOptions
        {
            DashboardCacheMinutes = cacheMinutes,
            TopBooksLimit = booksLimit,
            TopCategoriesLimit = categoriesLimit,
            PurchaseCandidatesLimit = 10
        };

        var result = new ReportingOptionsValidator()
            .Validate(null, options);

        Assert.False(result.Failed);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 10, 5)]
    [InlineData(-1, 10, 5)]
    [InlineData(61, 10, 5)]
    [InlineData(5, 0, 5)]
    [InlineData(5, -1, 5)]
    [InlineData(5, 101, 5)]
    [InlineData(5, 10, 0)]
    [InlineData(5, 10, -1)]
    [InlineData(5, 10, 101)]
    public void Rejects_invalid_options(
        int cacheMinutes,
        int booksLimit,
        int categoriesLimit)
    {
        var options = new ReportingOptions
        {
            DashboardCacheMinutes = cacheMinutes,
            TopBooksLimit = booksLimit,
            TopCategoriesLimit = categoriesLimit,
            PurchaseCandidatesLimit = 10
        };

        var result = new ReportingOptionsValidator()
            .Validate(null, options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Accepts_a_purchase_candidates_limit_from_1_to_100(int limit)
    {
        var options = new ReportingOptions
        {
            DashboardCacheMinutes = 5,
            TopBooksLimit = 10,
            TopCategoriesLimit = 5,
            PurchaseCandidatesLimit = limit
        };

        var result = new ReportingOptionsValidator()
            .Validate(null, options);

        Assert.False(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_an_invalid_purchase_candidates_limit(int limit)
    {
        var options = new ReportingOptions
        {
            DashboardCacheMinutes = 5,
            TopBooksLimit = 10,
            TopCategoriesLimit = 5,
            PurchaseCandidatesLimit = limit
        };

        var result = new ReportingOptionsValidator()
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Reporting:PurchaseCandidatesLimit", result.FailureMessage);
    }
}
