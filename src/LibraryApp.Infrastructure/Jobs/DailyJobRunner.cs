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
/// <item>once per day across instances: the run is claimed in <see cref="JobRunStore"/>, and the lease of the claim is
/// extended every third of <c>Jobs:Lease</c> while the job works, so a long job is never taken over by another instance;</item>
/// <item>catches up: an instance that was down at the set time runs the job at its next check;</item>
/// <item>a failed run is tried again after <c>Jobs:RetryDelay</c>, up to <c>Jobs:MaxAttemptsPerDay</c> attempts;</item>
/// <item>a finished run whose result could not be saved is not run again: the result is saved at the next check;</item>
/// <item>claim, work and result each have their own DI scope; the job decides its own transactions.</item>
/// </list>
/// A run is repeated only when its instance stops mid-run (another instance takes it over after the lease), so the work of
/// a job must be idempotent (NOT-06, BOR-06).
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
    private UnsavedSuccess? _unsavedSuccess;

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
        if (_unsavedSuccess is { } unsaved)
        {
            // Throws while the database is still unavailable: nothing else runs until the result is saved.
            await SaveSuccessAsync(unsaved.Claim, unsaved.Summary);
            _unsavedSuccess = null;
        }

        JobsOptions settings = options.Value;
        JobScheduleOptions schedule = settings.Schedules[job.Name];
        DateOnly today = clock.LocalToday;

        if (schedule.WorkingDaysOnly == true && !calendar.IsWorkingDay(today))
            return false;
        if (clock.UtcNow < clock.StartOfLocalDayUtc(today) + schedule.RunAt!.Value.ToTimeSpan())
            return false;

        JobRunClaim? claim;
        DateTime? lastSuccess;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<JobRunStore>();
            claim = await store.TryStartAsync(job.Name, today, settings, ct);
            if (claim is null)
                return false;
            lastSuccess = await store.LastSuccessfulStartAsync(job.Name, ct);
        }

        logger.LogInformation("Daily job {Job} started for {LocalDate} (attempt {Attempt})", job.Name, today, claim.Attempt);

        string summary;
        using (var work = CancellationTokenSource.CreateLinkedTokenSource(ct))
        using (var heartbeatStop = new CancellationTokenSource())
        {
            Task heartbeat = KeepLeaseAsync(claim, settings.Lease, work, heartbeatStop.Token);
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                var dailyJob = (IDailyJob)scope.ServiceProvider.GetRequiredService(job.JobType);
                summary = await dailyJob.RunAsync(new DailyJobRun(job.Name, today, lastSuccess), work.Token);
            }
            catch (Exception ex)
            {
                await StopAsync(heartbeatStop, heartbeat);
                await RecordFailureAsync(claim, ex, leaseLost: work.IsCancellationRequested && !ct.IsCancellationRequested, settings, ct);
                return true;
            }

            await StopAsync(heartbeatStop, heartbeat);
        }

        // Separate from the work: a result that could not be saved must not turn a finished run into a failed one,
        // or the next attempt would do the work again (for example send the same emails twice).
        try
        {
            await SaveSuccessAsync(claim, summary);
        }
        catch (Exception ex)
        {
            _unsavedSuccess = new UnsavedSuccess(claim, summary);
            logger.LogError(ex, "Daily job {Job} finished for {LocalDate} but its result could not be saved; saving it again at the next check",
                job.Name, today);
        }

        return true;
    }

    private async Task SaveSuccessAsync(JobRunClaim claim, string summary)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        bool saved = await scope.ServiceProvider.GetRequiredService<JobRunStore>()
            .SucceedAsync(claim, summary, CancellationToken.None);

        if (saved)
            logger.LogInformation("Daily job {Job} finished for {LocalDate}: {Summary}", job.Name, claim.LocalDate, summary);
        else
            logger.LogWarning("Daily job {Job} finished for {LocalDate} after another instance had taken the run over (attempt {Attempt}); its result is not recorded",
                job.Name, claim.LocalDate, claim.Attempt);
    }

    private async Task RecordFailureAsync(JobRunClaim claim, Exception ex, bool leaseLost, JobsOptions settings, CancellationToken ct)
    {
        if (leaseLost)
        {
            logger.LogWarning("Daily job {Job} for {LocalDate} was stopped: another instance has taken the run over", job.Name, claim.LocalDate);
            return;
        }

        bool stopping = ex is OperationCanceledException && ct.IsCancellationRequested;
        string error = stopping ? "Stopped: the application shut down during the run." : $"{ex.GetType().Name}: {ex.Message}";
        if (stopping)
            logger.LogWarning("Daily job {Job} for {LocalDate} was stopped by the shutdown of the application", job.Name, claim.LocalDate);
        else if (claim.Attempt >= settings.MaxAttemptsPerDay)
            logger.LogCritical(ex, "Daily job {Job} failed for {LocalDate} on its last attempt ({Attempt}); it does not run again for this date",
                job.Name, claim.LocalDate, claim.Attempt);
        else
            logger.LogError(ex, "Daily job {Job} failed for {LocalDate} (attempt {Attempt})", job.Name, claim.LocalDate, claim.Attempt);

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<JobRunStore>().FailAsync(claim, error, CancellationToken.None);
        }
        catch (Exception recordEx)
        {
            // The run stays Running: another instance (or this one) takes it over when the lease ends.
            logger.LogError(recordEx, "Daily job {Job}: the failure for {LocalDate} could not be recorded", job.Name, claim.LocalDate);
        }

        if (stopping)
            throw new OperationCanceledException("The application is shutting down.", ex, ct);
    }

    /// <summary>
    /// Extends the lease every third of <c>Jobs:Lease</c> while the job works. When the run is no longer this instance's
    /// (its lease ended while the database was unreachable and another instance took the run over), the job is cancelled.
    /// </summary>
    private async Task KeepLeaseAsync(JobRunClaim claim, TimeSpan lease, CancellationTokenSource work, CancellationToken stop)
    {
        TimeSpan every = lease / 3;
        while (true)
        {
            try
            {
                await Task.Delay(every, time, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                bool extended = await scope.ServiceProvider.GetRequiredService<JobRunStore>().ExtendLeaseAsync(claim, lease, stop);
                if (!extended)
                {
                    await work.CancelAsync();
                    return;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Tried again at the next beat; the lease still has two thirds left.
                logger.LogWarning(ex, "Daily job {Job}: the lease for {LocalDate} could not be extended", job.Name, claim.LocalDate);
            }
        }
    }

    private static async Task StopAsync(CancellationTokenSource heartbeatStop, Task heartbeat)
    {
        await heartbeatStop.CancelAsync();
        await heartbeat;
    }

    private sealed record UnsavedSuccess(JobRunClaim Claim, string Summary);
}
