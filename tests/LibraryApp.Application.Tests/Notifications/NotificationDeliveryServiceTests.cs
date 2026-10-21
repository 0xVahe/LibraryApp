using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Application.Notifications.Options;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class NotificationDeliveryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Sends_pending_email_and_marks_it_sent()
    {
        var store = new FakeStore(NewNotification("anna@ameriabank.am", "manager@ameriabank.am"));
        var sender = new FakeSender();

        var tried = await Service(store, sender).DeliverPendingAsync();

        var message = Assert.Single(sender.Sent);
        Assert.Equal("anna@ameriabank.am", message.To);
        Assert.Equal("manager@ameriabank.am", message.Cc);
        Assert.Equal("Subject", message.Subject);
        Assert.Equal("<p>Body</p>", message.HtmlBody);
        Assert.Equal(1, tried);
        Assert.Equal(NotificationStatus.Sent, store.Notifications[0].Status);
        Assert.Equal(Now, store.Notifications[0].SentAtUtc);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 15)]
    [InlineData(2, 60)]
    public async Task Failed_email_gets_its_next_retry(int failedBefore, int delayMinutes)
    {
        var notification = NewNotification();
        for (int i = 0; i < failedBefore; i++)
            notification.MarkFailed("Earlier failure.", Now);
        var store = new FakeStore(notification);

        await Service(store, new FakeSender { FailFor = "anna@ameriabank.am" }).DeliverPendingAsync();

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(failedBefore + 1, notification.Attempts);
        Assert.Equal(Now.AddMinutes(delayMinutes), notification.NextAttemptAtUtc);
        Assert.Equal("Mail server is down.", notification.LastError);
    }

    [Fact]
    public async Task After_the_last_retry_the_email_stays_failed()
    {
        var notification = NewNotification();
        for (int i = 0; i < 3; i++)
            notification.MarkFailed("Earlier failure.", Now);
        var store = new FakeStore(notification);

        await Service(store, new FakeSender { FailFor = "anna@ameriabank.am" }).DeliverPendingAsync();

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(4, notification.Attempts);
        Assert.Null(notification.NextAttemptAtUtc);
    }

    [Fact]
    public async Task A_retry_that_succeeds_marks_the_email_sent()
    {
        var notification = NewNotification();
        notification.MarkFailed("Earlier failure.", Now);
        var store = new FakeStore(notification);

        await Service(store, new FakeSender()).DeliverPendingAsync();

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(Now, notification.SentAtUtc);
        Assert.Null(notification.NextAttemptAtUtc);
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_others()
    {
        var store = new FakeStore(NewNotification("anna@ameriabank.am"), NewNotification("gor@ameriabank.am"));
        var sender = new FakeSender { FailFor = "anna@ameriabank.am" };

        var tried = await Service(store, sender).DeliverPendingAsync();

        Assert.Equal(2, tried);
        Assert.Equal(NotificationStatus.Failed, store.Notifications[0].Status);
        Assert.Equal(NotificationStatus.Sent, store.Notifications[1].Status);
        Assert.Equal(2, store.Saves);
    }

    [Fact]
    public async Task Claims_one_batch_with_the_lease()
    {
        var store = new FakeStore();

        var tried = await Service(store, new FakeSender()).DeliverPendingAsync();

        Assert.Equal(20, store.RequestedMax);
        Assert.Equal(Now, store.RequestedNow);
        Assert.Equal(Now.AddMinutes(15), store.RequestedLeaseUntil);
        Assert.Equal(0, tried);
    }

    [Fact]
    public async Task Cancellation_is_not_a_failure()
    {
        var store = new FakeStore(NewNotification());
        var sender = new FakeSender { Cancel = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(store, sender).DeliverPendingAsync());

        Assert.Equal(NotificationStatus.Pending, store.Notifications[0].Status);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Stops_when_the_lease_has_ended()
    {
        var clock = new TestClock(Now);
        var store = new FakeStore(NewNotification("anna@ameriabank.am"), NewNotification("gor@ameriabank.am"));
        var sender = new FakeSender { OnSent = () => clock.UtcNow = Now.AddMinutes(15) };

        await Service(store, sender, clock).DeliverPendingAsync();

        Assert.Single(sender.Sent);
        Assert.Equal(NotificationStatus.Sent, store.Notifications[0].Status);
        Assert.Equal(NotificationStatus.Pending, store.Notifications[1].Status);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Sent_status_is_saved_even_when_the_app_is_stopping()
    {
        using var stopping = new CancellationTokenSource();
        var store = new FakeStore(NewNotification());
        var sender = new FakeSender { OnSent = stopping.Cancel };

        await Service(store, sender).DeliverPendingAsync(stopping.Token);

        Assert.Equal(NotificationStatus.Sent, store.Notifications[0].Status);
        Assert.Equal(1, store.Saves);
    }

    private static NotificationDeliveryService Service(FakeStore store, FakeSender sender, TestClock? clock = null) =>
        new(store, sender, clock ?? new TestClock(Now), Settings, NullLogger<NotificationDeliveryService>.Instance);

    private static readonly IOptions<NotificationsOptions> Settings = Microsoft.Extensions.Options.Options.Create(new NotificationsOptions
    {
        Delivery = new DeliveryOptions
        {
            PollInterval = TimeSpan.FromSeconds(5),
            BatchSize = 20,
            Lease = TimeSpan.FromMinutes(15),
            RetryDelays = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)],
        },
    });

    private static Notification NewNotification(string to = "anna@ameriabank.am", string? cc = null) =>
        Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), NotificationType.LoanConfirmation, NotificationChannel.Outlook,
            to, cc, "Subject", "<p>Body</p>", Now.AddMinutes(-1), DateOnly.FromDateTime(Now));

    private sealed class FakeStore(params Notification[] notifications) : INotificationDeliveryStore
    {
        public List<Notification> Notifications { get; } = [.. notifications];
        public int RequestedMax { get; private set; }
        public DateTime RequestedNow { get; private set; }
        public DateTime RequestedLeaseUntil { get; private set; }
        public int Saves { get; private set; }

        public Task<IReadOnlyList<Notification>> ClaimDueAsync(
            DateTime nowUtc, DateTime leaseUntilUtc, int max, CancellationToken ct = default)
        {
            RequestedMax = max;
            RequestedNow = nowUtc;
            RequestedLeaseUntil = leaseUntilUtc;
            return Task.FromResult<IReadOnlyList<Notification>>(Notifications.Take(max).ToList());
        }

        public Task SaveAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();   // like EF Core
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender : IOutlookNotificationSender
    {
        public List<OutlookMessage> Sent { get; } = [];
        public string? FailFor { get; init; }
        public bool Cancel { get; init; }
        public Action? OnSent { get; init; }

        public Task SendAsync(OutlookMessage message, CancellationToken ct = default)
        {
            if (Cancel)
                throw new OperationCanceledException();
            if (message.To == FailFor)
                throw new NotificationDeliveryException("Mail server is down.");

            Sent.Add(message);
            OnSent?.Invoke();
            return Task.CompletedTask;
        }
    }
}
