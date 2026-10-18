using LibraryApp.Application.Notifications.Options;
using LibraryApp.Application.Notifications.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Jobs.Notifications;

/// <summary>Sends Pending Outlook notifications in the background (NOT-03). Off when Jobs:Enabled is false.</summary>
public sealed class NotificationDeliveryJob(
    IServiceScopeFactory scopes,
    IOptions<NotificationsOptions> options,
    IOptions<JobsOptions> jobs,
    ILogger<NotificationDeliveryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!jobs.Value.Enabled)
        {
            logger.LogInformation("Notification delivery is disabled");
            return;
        }

        DeliveryOptions settings = options.Value.Delivery;

        while (!stoppingToken.IsCancellationRequested)
        {
            var tried = 0;
            try
            {
                tried = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A database problem must not stop the loop.
                logger.LogError(ex, "Notification delivery round failed");
            }

            // A full batch means more may be waiting: go on at once.
            if (tried >= settings.BatchSize)
                continue;

            try
            {
                await Task.Delay(settings.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var delivery = scope.ServiceProvider.GetRequiredService<INotificationDeliveryService>();
        return await delivery.DeliverPendingAsync(options.Value.Delivery.BatchSize, ct);
    }
}
