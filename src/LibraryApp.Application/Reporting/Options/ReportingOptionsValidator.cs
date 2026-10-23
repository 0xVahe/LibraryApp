using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Reporting.Options;

public sealed class ReportingOptionsValidator : IValidateOptions<ReportingOptions>
{
    public ValidateOptionsResult Validate(string? name, ReportingOptions options)
    {
        List<string> failures = [];

        if (options.DashboardCacheMinutes is < 1 or > 60)
            failures.Add("Reporting:DashboardCacheMinutes must be between 1 and 60.");

        if (options.TopBooksLimit is < 1 or > 100)
            failures.Add("Reporting:TopBooksLimit must be between 1 and 100.");

        if (options.TopCategoriesLimit is < 1 or > 100)
            failures.Add("Reporting:TopCategoriesLimit must be between 1 and 100.");

        if (options.PurchaseCandidatesLimit is < 1 or > 100)
            failures.Add("Reporting:PurchaseCandidatesLimit must be between 1 and 100.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
