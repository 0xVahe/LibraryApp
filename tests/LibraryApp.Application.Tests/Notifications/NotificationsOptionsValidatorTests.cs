using LibraryApp.Application.Notifications.Options;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class NotificationsOptionsValidatorTests
{
    private static readonly NotificationsOptionsValidator Validator = new();

    [Fact]
    public void Valid_settings_pass()
    {
        var result = Validator.Validate(null, Options(TimeSpan.FromSeconds(5), 20));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_settings_fail_with_both_keys()
    {
        var result = Validator.Validate(null, new NotificationsOptions());

        Assert.True(result.Failed);
        Assert.Contains("Notifications:Delivery:PollInterval", result.FailureMessage);
        Assert.Contains("Notifications:Delivery:BatchSize", result.FailureMessage);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(3601, 20)]
    [InlineData(5, 0)]
    [InlineData(5, 101)]
    public void Out_of_range_fails(int pollSeconds, int batchSize)
    {
        var result = Validator.Validate(null, Options(TimeSpan.FromSeconds(pollSeconds), batchSize));

        Assert.True(result.Failed);
    }

    private static NotificationsOptions Options(TimeSpan pollInterval, int batchSize) =>
        new() { Delivery = new DeliveryOptions { PollInterval = pollInterval, BatchSize = batchSize } };
}
