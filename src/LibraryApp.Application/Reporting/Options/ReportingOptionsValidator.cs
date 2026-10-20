using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Reporting.Options;

public sealed class ReportingOptionsValidator : IValidateOptions<ReportingOptions>
{
    public ValidateOptionsResult Validate(string? name, ReportingOptions options)
    {
        List<string> failures = [];

        if (options.DashboardCacheMinutes <= 0)
            failures.Add("Reporting:DashboardCacheMinutes must be positive.");

        if (options.TopBooksLimit <= 0)
            failures.Add("Reporting:TopBooksLimit must be positive.");

        if (options.TopCategoriesLimit <= 0)
            failures.Add("Reporting:TopCategoriesLimit must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
