using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Notifications.Options;

/// <summary>A missing value binds as 0 and fails here.</summary>
public sealed class NotificationsOptionsValidator : IValidateOptions<NotificationsOptions>
{
    private const string Delivery = NotificationsOptions.SectionName + ":" + nameof(NotificationsOptions.Delivery);

    public ValidateOptionsResult Validate(string? name, NotificationsOptions options)
    {
        var failures = new List<string>();
        DeliveryOptions delivery = options.Delivery;

        if (delivery.PollInterval < TimeSpan.FromSeconds(1) || delivery.PollInterval > TimeSpan.FromHours(1))
            failures.Add($"{Delivery}:{nameof(delivery.PollInterval)} must be between 00:00:01 and 01:00:00 " +
                         $"(found {delivery.PollInterval}); set it in appsettings.json.");

        if (delivery.BatchSize < 1 || delivery.BatchSize > 100)
            failures.Add($"{Delivery}:{nameof(delivery.BatchSize)} must be between 1 and 100 " +
                         $"(found {delivery.BatchSize}); set it in appsettings.json.");

        if (delivery.Lease < TimeSpan.FromMinutes(1) || delivery.Lease > TimeSpan.FromHours(1))
            failures.Add($"{Delivery}:{nameof(delivery.Lease)} must be between 00:01:00 and 01:00:00 " +
                         $"(found {delivery.Lease}); set it in appsettings.json.");

        if (delivery.RetryDelays.Length is < 1 or > 10 ||
            delivery.RetryDelays.Any(d => d < TimeSpan.FromMinutes(1) || d > TimeSpan.FromDays(1)))
            failures.Add($"{Delivery}:{nameof(delivery.RetryDelays)} must have 1 to 10 values, each between 00:01:00 and " +
                         $"1.00:00:00 (found {delivery.RetryDelays.Length} values); set it in appsettings.json.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
