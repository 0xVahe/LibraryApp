using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Ui;

/// <summary>Settings section "Ui" of the browser UI (SD-21). Checked at start.</summary>
public sealed class UiOptions
{
    public const string SectionName = "Ui";

    /// <summary>
    /// How often the sign-in cookie is checked against the account's security stamp. A password change or a
    /// deactivation ends the cookie session at most this long after it happens. Zero checks every request.
    /// </summary>
    public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class UiOptionsValidator : IValidateOptions<UiOptions>
{
    public ValidateOptionsResult Validate(string? name, UiOptions options) =>
        options.SecurityStampValidationInterval < TimeSpan.Zero
            ? ValidateOptionsResult.Fail(
                $"{UiOptions.SectionName}:{nameof(UiOptions.SecurityStampValidationInterval)} must not be negative " +
                $"(found {options.SecurityStampValidationInterval}).")
            : ValidateOptionsResult.Success;
}
