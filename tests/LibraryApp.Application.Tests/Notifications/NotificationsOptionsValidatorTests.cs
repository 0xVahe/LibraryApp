using LibraryApp.Application.Notifications.Options;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class NotificationsOptionsValidatorTests
{
    private static readonly NotificationsOptionsValidator Validator = new();

    [Fact]
    public void Valid_settings_pass()
    {
        var result = Validator.Validate(null, Options(TimeSpan.FromSeconds(5), 20, TimeSpan.FromMinutes(15)));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_settings_fail_with_both_keys()
    {
        var result = Validator.Validate(null, new NotificationsOptions());

        Assert.True(result.Failed);
        Assert.Contains("Notifications:Delivery:PollInterval", result.FailureMessage);
        Assert.Contains("Notifications:Delivery:BatchSize", result.FailureMessage);
        Assert.Contains("Notifications:Delivery:Lease", result.FailureMessage);
    }

    [Theory]
    [InlineData(0, 20, 15)]
    [InlineData(3601, 20, 15)]
    [InlineData(5, 0, 15)]
    [InlineData(5, 101, 15)]
    [InlineData(5, 20, 0)]
    [InlineData(5, 20, 61)]
    public void Out_of_range_fails(int pollSeconds, int batchSize, int leaseMinutes)
    {
        var result = Validator.Validate(
            null, Options(TimeSpan.FromSeconds(pollSeconds), batchSize, TimeSpan.FromMinutes(leaseMinutes)));

        Assert.True(result.Failed);
    }

    private static NotificationsOptions Options(TimeSpan pollInterval, int batchSize, TimeSpan lease) =>
        new() { Delivery = new DeliveryOptions { PollInterval = pollInterval, BatchSize = batchSize, Lease = lease } };
}
