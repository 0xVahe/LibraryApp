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
    public void Missing_retry_delays_fail_with_the_key()
    {
        var options = Options(TimeSpan.FromSeconds(5), 20, TimeSpan.FromMinutes(15));
        options.Delivery.RetryDelays = [];

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Notifications:Delivery:RetryDelays", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void Retry_delay_out_of_range_fails(int minutes)
    {
        var options = Options(TimeSpan.FromSeconds(5), 20, TimeSpan.FromMinutes(15));
        options.Delivery.RetryDelays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(minutes)];

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Missing_settings_fail_with_every_key()
    {
        var result = Validator.Validate(null, new NotificationsOptions());

        Assert.True(result.Failed);
        Assert.Contains("Notifications:Delivery:PollInterval", result.FailureMessage);
        Assert.Contains("Notifications:Delivery:BatchSize", result.FailureMessage);
        Assert.Contains("Notifications:Delivery:Lease", result.FailureMessage);
        Assert.Contains("Notifications:Delivery:RetryDelays", result.FailureMessage);
        Assert.Contains("Notifications:Reminders:DueSoonWorkingDays", result.FailureMessage);
        Assert.Contains("Notifications:Reminders:OverdueEveryWorkingDays", result.FailureMessage);
        Assert.Contains("Notifications:Reminders:LineManagerCcAfterWorkingDays", result.FailureMessage);
    }

    [Theory]
    [InlineData(0, 5, 14)]
    [InlineData(11, 5, 14)]
    [InlineData(2, 0, 14)]
    [InlineData(2, 31, 14)]
    [InlineData(2, 5, 0)]
    [InlineData(2, 5, 61)]
    public void Reminder_working_days_out_of_range_fail(int dueSoon, int overdueEvery, int lineManagerCcAfter)
    {
        var options = Options(TimeSpan.FromSeconds(5), 20, TimeSpan.FromMinutes(15));
        options.Reminders = new RemindersOptions
        {
            DueSoonWorkingDays = dueSoon,
            OverdueEveryWorkingDays = overdueEvery,
            LineManagerCcAfterWorkingDays = lineManagerCcAfter,
        };

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Notifications:Reminders:", result.FailureMessage);
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
        new()
        {
            Delivery = new DeliveryOptions
            {
                PollInterval = pollInterval,
                BatchSize = batchSize,
                Lease = lease,
                RetryDelays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)],
            },
            Reminders = new RemindersOptions
            {
                DueSoonWorkingDays = 2,
                OverdueEveryWorkingDays = 5,
                LineManagerCcAfterWorkingDays = 14,
            },
        };
}
