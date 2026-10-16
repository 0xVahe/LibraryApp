using LibraryApp.Application.Abstractions.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Jobs;

/// <summary>
/// Runs one registered <see cref="IDailyJob"/> once per local day (SD-10), on every API instance in the background:
/// <list type="bullet">
/// <item>due from <c>Jobs:Schedules:&lt;name&gt;:RunAt</c> (Asia/Yerevan), on working days only when the schedule says so;</item>
/// <item>exactly once per day across instances: the run is claimed in <see cref="JobRunStore"/>;</item>
/// <item>catches up: an instance that was down at the set time runs the job at its next check;</item>
/// <item>a failed run is tried again after <c>Jobs:RetryDelay</c>, up to <c>Jobs:MaxAttemptsPerDay</c> attempts;</item>
/// <item>each run has its own DI scope; the job decides its own transactions.</item>
/// </list>
/// </summary>
public sealed class DailyJobRunner(
    DailyJobRegistration job,
    IServiceScopeFactory scopes,
    IClock clock,
    IWorkingDayCalendar calendar,
    IOptions<JobsOptions> options,
    TimeProvider time,
    ILogger<DailyJobRunner> logger) : BackgroundService
{
    public string JobName => job.Name;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobsOptions settings = options.Value;
        if (!settings.Enabled || settings.Schedules[job.Name].Enabled != true)
        {
            logger.LogInformation("Daily job {Job} is disabled", job.Name);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryRunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Claiming or recording failed (for example the database is down): try again at the next check.
                logger.LogError(ex, "Daily job {Job} could not be started", job.Name);
            }

            try
            {
                await Task.Delay(settings.PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs the job when it is due and not yet done today. Returns true when this call ran it.</summary>
    public async Task<bool> TryRunAsync(CancellationToken ct)
    {
        JobsOptions settings = options.Value;
        JobScheduleOptions schedule = settings.Schedules[job.Name];
        DateOnly today = clock.LocalToday;

        if (schedule.WorkingDaysOnly == true && !calendar.IsWorkingDay(today))
            return false;
        if (clock.UtcNow < clock.StartOfLocalDayUtc(today) + schedule.RunAt!.Value.ToTimeSpan())
            return false;

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<JobRunStore>();

        JobRun? claimed = await store.TryStartAsync(job.Name, today, settings, ct);
        if (claimed is null)
            return false;

        DateTime? lastSuccess = await store.LastSuccessfulStartAsync(job.Name, ct);
        logger.LogInformation("Daily job {Job} started for {LocalDate} (attempt {Attempt})", job.Name, today, claimed.Attempts);

        try
        {
            var work = (IDailyJob)scope.ServiceProvider.GetRequiredService(job.JobType);
            string summary = await work.RunAsync(new DailyJobRun(job.Name, today, lastSuccess), ct);
            await store.SucceedAsync(claimed.Id, summary, ct);
            logger.LogInformation("Daily job {Job} finished for {LocalDate}: {Summary}", job.Name, today, summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Daily job {Job} failed for {LocalDate}", job.Name, today);

            // A fresh scope: the job's DbContext may still hold the changes that failed.
            await using AsyncServiceScope failureScope = scopes.CreateAsyncScope();
            await failureScope.ServiceProvider.GetRequiredService<JobRunStore>()
                .FailAsync(claimed.Id, $"{ex.GetType().Name}: {ex.Message}", CancellationToken.None);
        }

        return true;
    }
}
