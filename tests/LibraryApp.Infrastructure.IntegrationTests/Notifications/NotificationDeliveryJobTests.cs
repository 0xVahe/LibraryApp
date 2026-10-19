using LibraryApp.Application.Notifications.Options;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Infrastructure.Jobs;
using LibraryApp.Infrastructure.Jobs.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

public sealed class NotificationDeliveryJobTests
{
    private const int BatchSize = 20;

    [Fact]
    public async Task Does_not_run_when_jobs_are_disabled()
    {
        var delivery = new FakeDelivery();

        // Long enough for several rounds if the job did run.
        await RunAsync(delivery, jobsEnabled: false, TimeSpan.FromMilliseconds(10), until: () => false, maxWait: TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, delivery.Calls);
    }

    [Fact]
    public async Task Full_batch_goes_on_at_once()
    {
        // A long poll interval: a second round only comes at once if a full batch skips the wait.
        var delivery = new FakeDelivery(BatchSize, 0);

        await RunAsync(delivery, jobsEnabled: true, TimeSpan.FromHours(1), until: () => delivery.Calls >= 2);

        Assert.Equal(2, delivery.Calls);
    }

    [Fact]
    public async Task Keeps_running_after_a_failed_round()
    {
        var delivery = new FakeDelivery { FailFirstCall = true };

        await RunAsync(delivery, jobsEnabled: true, TimeSpan.FromMilliseconds(10), until: () => delivery.Calls >= 2);

        Assert.True(delivery.Calls >= 2);
    }

    private static async Task RunAsync(
        FakeDelivery delivery, bool jobsEnabled, TimeSpan pollInterval, Func<bool> until, TimeSpan? maxWait = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<INotificationDeliveryService>(_ => delivery);
        await using var provider = services.BuildServiceProvider();

        var settings = Options.Create(new NotificationsOptions
        {
            Delivery = new DeliveryOptions { PollInterval = pollInterval, BatchSize = BatchSize, Lease = TimeSpan.FromMinutes(15) },
        });
        var job = new NotificationDeliveryJob(
            provider.GetRequiredService<IServiceScopeFactory>(), settings,
            Options.Create(new JobsOptions { Enabled = jobsEnabled }), NullLogger<NotificationDeliveryJob>.Instance);

        await job.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + (maxWait ?? TimeSpan.FromSeconds(5));
        while (!until() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await job.StopAsync(CancellationToken.None);
    }

    private sealed class FakeDelivery(params int[] results) : INotificationDeliveryService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public bool FailFirstCall { get; init; }

        public Task<int> DeliverPendingAsync(CancellationToken ct = default)
        {
            int call = Interlocked.Increment(ref _calls);
            if (FailFirstCall && call == 1)
                throw new InvalidOperationException("Database is down.");

            return Task.FromResult(call <= results.Length ? results[call - 1] : 0);
        }
    }
}
