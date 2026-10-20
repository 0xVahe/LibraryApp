using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Staff.Options;

public sealed class StaffOptionsValidator : IValidateOptions<StaffOptions>
{
    public ValidateOptionsResult Validate(string? name, StaffOptions options)
    {
        var failures = new List<string>();
        if (options.Import is null)
            failures.Add("Staff:Import is required.");
        else
        {
            if (options.Import.MaxRows <= 0)
                failures.Add("Staff:Import:MaxRows is required and must be greater than zero.");
            // The parser buffers the CSV in memory; its buffer uses Int32 indexing.
            if (options.Import.MaxFileBytes is <= 0 or > int.MaxValue)
                failures.Add($"Staff:Import:MaxFileBytes is required and must be between 1 and {int.MaxValue}.");
        }

        if (options.RestrictedLimits is null)
            failures.Add("Staff:RestrictedLimits is required.");
        else
        {
            if (options.RestrictedLimits.MaxLoans <= 0)
                failures.Add("Staff:RestrictedLimits:MaxLoans is required and must be greater than zero.");
            if (options.RestrictedLimits.MaxRenewals is null or < 0)
                failures.Add("Staff:RestrictedLimits:MaxRenewals is required and must be zero or greater.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
