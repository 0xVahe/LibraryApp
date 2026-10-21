using System.Collections.Concurrent;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.IntegrationTests.Time;
using LibraryApp.Infrastructure.Jobs;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Jobs;

/// <summary>The platform as the API registers it, with a clock the tests move, on a throw-away database.</summary>
public sealed class JobsFixture : IAsyncLifetime
{
    public ManualTimeProvider Time { get; } = new(ManualTimeProvider.Yerevan(2026, 10, 12, 10, 0));

    public ServiceProvider Services { get; private set; } = null!;

    public JobLog Log => Services.GetRequiredService<JobLog>();

    public FailingJobRunSaves FailingSaves => Services.GetRequiredService<FailingJobRunSaves>();

    public RunnerLogger Logger { get; } = new();

    /// <summary>The "Jobs" settings of the fixture: lease 1 hour, retry after 15 minutes, 3 attempts a day.</summary>
    public JobsOptions Settings => Services.GetRequiredService<IOptions<JobsOptions>>().Value;

    public async Task InitializeAsync()
    {
        string connectionString = TestDatabase.NewDatabase();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:LibraryDb"] = connectionString,
            ["Messaging:DispatcherEnabled"] = "false",
            ["Time:TimeZoneId"] = "Asia/Yerevan",
            ["WorkingDayCalendar:Holidays:0"] = "2026-09-21",   // Independence Day, a Monday
            ["Jobs:Enabled"] = "true",
            ["Jobs:PollInterval"] = "00:01:00",
            ["Jobs:Lease"] = "01:00:00",
            ["Jobs:RetryDelay"] = "00:15:00",
            ["Jobs:MaxAttemptsPerDay"] = "3",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatform(configuration, new TestEnvironment());
        services.AddSingleton<TimeProvider>(Time);   // replaces TimeProvider.System
        services.AddSingleton<JobLog>();
        services.AddScoped<CountingJob>();

        // The same SQL Server options, plus a switch that makes saving a successful run fail (database unreachable).
        services.AddSingleton<FailingJobRunSaves>();
        services.RemoveAll<DbContextOptions<LibraryDbContext>>();
        services.AddScoped(provider =>
        {
            var builder = new DbContextOptionsBuilder<LibraryDbContext>();
            builder.UseLibrarySqlServer(connectionString).AddInterceptors(provider.GetRequiredService<FailingJobRunSaves>());
            return builder.Options;
        });
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }

    /// <summary>A runner for a job with its own name and schedule, as AddDailyJob would create it.</summary>
    public DailyJobRunner NewRunner(
        TimeOnly runAt, bool workingDaysOnly = false, string? name = null, bool jobsEnabled = true, bool scheduleEnabled = true)
    {
        name ??= $"test-{Guid.NewGuid():N}";
        var settings = new JobsOptions
        {
            Enabled = jobsEnabled,
            PollInterval = Settings.PollInterval,
            Lease = Settings.Lease,
            RetryDelay = Settings.RetryDelay,
            MaxAttemptsPerDay = Settings.MaxAttemptsPerDay,
            Schedules = { [name] = new JobScheduleOptions { Enabled = scheduleEnabled, RunAt = runAt, WorkingDaysOnly = workingDaysOnly } },
        };
        return new DailyJobRunner(
            new DailyJobRegistration(name, typeof(CountingJob)),
            Services.GetRequiredService<IServiceScopeFactory>(),
            Services.GetRequiredService<IClock>(),
            Services.GetRequiredService<IWorkingDayCalendar>(),
            Options.Create(settings),
            Time,
            Logger);
    }

    public async Task<JobRun> RunRecordAsync(string jobName, DateOnly date)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().JobRuns.AsNoTracking()
            .SingleAsync(r => r.JobName == jobName && r.LocalDate == date);
    }

    public async Task<T> WithStoreAsync<T>(Func<JobRunStore, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<JobRunStore>());
    }

    /// <summary>Waits (real time) for something a background task does; fails the test after 10 seconds.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Waited 10 seconds for: {what}");
            await Task.Delay(20);
        }
    }

    public static Task EventuallyAsync(Func<bool> condition, string what) => EventuallyAsync(() => Task.FromResult(condition()), what);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LibraryApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>What the test jobs did, and how each job name behaves.</summary>
public sealed class JobLog
{
    public ConcurrentDictionary<string, int> Runs { get; } = new();

    public ConcurrentDictionary<string, DailyJobRun> LastRun { get; } = new();

    /// <summary>The first run of these jobs throws.</summary>
    public ConcurrentDictionary<string, bool> FailFirst { get; } = new();

    /// <summary>Every run of these jobs throws.</summary>
    public ConcurrentDictionary<string, bool> FailAlways { get; } = new();

    /// <summary>These jobs signal <see cref="Started"/> and then work until the gate opens (or their token is cancelled).</summary>
    public ConcurrentDictionary<string, TaskCompletionSource> Gates { get; } = new();

    public ConcurrentDictionary<string, TaskCompletionSource> Started { get; } = new();

    /// <summary>A job that works until the test opens the gate.</summary>
    public (TaskCompletionSource Gate, Task Started) LongJob(string jobName)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Gates[jobName] = gate;
        Started[jobName] = started;
        return (gate, started.Task);
    }
}

public sealed class CountingJob(JobLog log) : IDailyJob
{
    public async Task<string> RunAsync(DailyJobRun run, CancellationToken ct)
    {
        int count = log.Runs.AddOrUpdate(run.JobName, 1, (_, runs) => runs + 1);
        log.LastRun[run.JobName] = run;

        if (log.Gates.TryGetValue(run.JobName, out TaskCompletionSource? gate))
        {
            log.Started[run.JobName].TrySetResult();
            await gate.Task.WaitAsync(ct);
        }

        if (log.FailAlways.ContainsKey(run.JobName) || (count == 1 && log.FailFirst.ContainsKey(run.JobName)))
            throw new InvalidOperationException("boom");
        return $"ran {count}";
    }
}

/// <summary>Makes saving a successful run of the listed jobs fail, as when the database is unreachable at that moment.</summary>
public sealed class FailingJobRunSaves : SaveChangesInterceptor
{
    /// <summary>Job name → how many saves of its success still fail.</summary>
    public ConcurrentDictionary<string, int> FailSuccessOf { get; } = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        eventData.Context!.ChangeTracker.DetectChanges();
        foreach (var entry in eventData.Context.ChangeTracker.Entries<JobRun>())
        {
            string jobName = entry.Entity.JobName;
            if (entry.State == EntityState.Modified && entry.Entity.Status == JobRunStatus.Succeeded
                && FailSuccessOf.TryGetValue(jobName, out int left) && left > 0)
            {
                FailSuccessOf[jobName] = left - 1;
                throw new InvalidOperationException("Simulated: the database is unreachable.");
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Keeps what the runners log, to check the level of a message.</summary>
public sealed class RunnerLogger : ILogger<DailyJobRunner>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IEnumerable<(LogLevel Level, string Message)> For(string jobName) => Entries.Where(e => e.Message.Contains(jobName, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception)));
}

/// <summary>
/// SD-10: a daily job runs once per local day across instances, at its set Asia/Yerevan time (BOR-06 00:30, NOT-05 09:00
/// on working days), catches up after downtime, retries a failure and never repeats a finished run (NOT-06).
/// </summary>
public class DailyJobRunnerTests(JobsFixture fixture) : IClassFixture<JobsFixture>
{
    private static readonly TimeOnly HalfPastMidnight = new(0, 30);

    [Fact]
    public async Task Runs_once_from_the_set_local_time_and_not_again_the_same_day()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 12, 0, 29));
        Assert.False(await runner.TryRunAsync(default));               // before 00:30 Yerevan

        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 12, 0, 31));
        Assert.True(await runner.TryRunAsync(default));
        Assert.False(await runner.TryRunAsync(default));               // already done today

        JobRun run = await fixture.RunRecordAsync(runner.JobName, new DateOnly(2026, 10, 12));
        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal("ran 1", run.Summary);
        Assert.Equal(1, fixture.Log.Runs[runner.JobName]);
    }

    [Fact]
    public async Task The_day_of_the_run_is_the_Yerevan_date_not_the_UTC_date()
    {
        // 00:30 on 21 October in Yerevan is 20:30 on 20 October in UTC (NFR-12).
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(new DateTimeOffset(2026, 10, 20, 20, 30, 0, TimeSpan.Zero));

        Assert.True(await runner.TryRunAsync(default));

        Assert.Equal(new DateOnly(2026, 10, 21), fixture.Log.LastRun[runner.JobName].LocalDate);
        Assert.Equal(JobRunStatus.Succeeded, (await fixture.RunRecordAsync(runner.JobName, new DateOnly(2026, 10, 21))).Status);
    }

    [Fact]
    public async Task The_next_day_runs_again_and_knows_when_the_last_run_started()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 13, 0, 30));
        Assert.True(await runner.TryRunAsync(default));
        Assert.Null(fixture.Log.LastRun[runner.JobName].LastSuccessfulStartUtc);   // first run ever

        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 14, 0, 30));
        Assert.True(await runner.TryRunAsync(default));

        DailyJobRun second = fixture.Log.LastRun[runner.JobName];
        Assert.Equal(new DateOnly(2026, 10, 14), second.LocalDate);
        Assert.Equal(ManualTimeProvider.Yerevan(2026, 10, 13, 0, 30).UtcDateTime, second.LastSuccessfulStartUtc);
    }

    [Fact]
    public async Task After_a_whole_day_of_downtime_the_next_run_knows_the_missed_day_was_not_processed()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 11, 2, 0, 30));
        Assert.True(await runner.TryRunAsync(default));

        // 3 November: every instance is down all day. 4 November: the job must also process what fell due on 3 November.
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 11, 4, 0, 30));
        Assert.True(await runner.TryRunAsync(default));

        Assert.Equal(ManualTimeProvider.Yerevan(2026, 11, 2, 0, 30).UtcDateTime, fixture.Log.LastRun[runner.JobName].LastSuccessfulStartUtc);
    }

    [Fact]
    public async Task An_instance_that_was_down_at_the_set_time_catches_up_when_it_starts()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 15, 15, 0));   // started in the afternoon

        Assert.True(await runner.TryRunAsync(default));
    }

    [Fact]
    public async Task A_failed_run_is_tried_again_after_the_retry_delay()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Log.FailFirst[runner.JobName] = true;
        var date = new DateOnly(2026, 10, 16);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 16, 0, 30));

        Assert.True(await runner.TryRunAsync(default));
        JobRun failed = await fixture.RunRecordAsync(runner.JobName, date);
        Assert.Equal(JobRunStatus.Failed, failed.Status);
        Assert.Contains("boom", failed.LastError);

        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(await runner.TryRunAsync(default));               // the retry delay (15 minutes) is not over

        fixture.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await runner.TryRunAsync(default));
        JobRun retried = await fixture.RunRecordAsync(runner.JobName, date);
        Assert.Equal(JobRunStatus.Succeeded, retried.Status);
        Assert.Equal(2, retried.Attempts);
        Assert.Null(retried.LastError);
    }

    [Fact]
    public async Task After_the_daily_attempts_fail_the_job_stops_for_the_day_and_runs_again_the_next_day()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Log.FailAlways[runner.JobName] = true;
        var date = new DateOnly(2026, 10, 22);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 22, 0, 30));

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(await runner.TryRunAsync(default));
            fixture.Time.Advance(TimeSpan.FromMinutes(16));
        }

        Assert.False(await runner.TryRunAsync(default));               // Jobs:MaxAttemptsPerDay = 3
        fixture.Time.Advance(TimeSpan.FromHours(5));
        Assert.False(await runner.TryRunAsync(default));

        JobRun run = await fixture.RunRecordAsync(runner.JobName, date);
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Equal(3, run.Attempts);
        Assert.Equal(3, fixture.Log.Runs[runner.JobName]);
        Assert.Single(fixture.Logger.For(runner.JobName), e => e.Level == LogLevel.Critical);   // the last failure alerts

        fixture.Log.FailAlways.TryRemove(runner.JobName, out _);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 23, 0, 30));
        Assert.True(await runner.TryRunAsync(default));                // the attempts count per day
    }

    [Fact]
    public async Task A_finished_run_whose_result_could_not_be_saved_is_not_run_again()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        var date = new DateOnly(2026, 10, 24);
        fixture.FailingSaves.FailSuccessOf[runner.JobName] = 1;
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 24, 0, 30));

        Assert.True(await runner.TryRunAsync(default));
        JobRun unsaved = await fixture.RunRecordAsync(runner.JobName, date);
        Assert.Equal(JobRunStatus.Running, unsaved.Status);           // not Failed: nothing to retry
        Assert.Contains(fixture.Logger.For(runner.JobName), e => e.Level == LogLevel.Error && e.Message.Contains("could not be saved"));

        fixture.Time.Advance(TimeSpan.FromMinutes(16));                // past the retry delay
        Assert.False(await runner.TryRunAsync(default));               // saves the result, does not run the job again

        JobRun saved = await fixture.RunRecordAsync(runner.JobName, date);
        Assert.Equal(JobRunStatus.Succeeded, saved.Status);
        Assert.Equal("ran 1", saved.Summary);
        Assert.Equal(1, saved.Attempts);
        Assert.Equal(1, fixture.Log.Runs[runner.JobName]);
    }

    [Fact]
    public async Task Two_instances_at_the_same_moment_run_the_job_once()
    {
        string name = $"same-{Guid.NewGuid():N}";
        var first = fixture.NewRunner(HalfPastMidnight, name: name);
        var second = fixture.NewRunner(HalfPastMidnight, name: name);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 19, 0, 30));

        bool[] results = await Task.WhenAll(first.TryRunAsync(default), second.TryRunAsync(default));

        Assert.Single(results, ran => ran);
        Assert.Equal(1, fixture.Log.Runs[name]);
    }

    [Fact]
    public async Task A_run_left_Running_by_a_stopped_instance_is_taken_over_after_the_lease()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        var date = new DateOnly(2026, 10, 20);
        DateTime started = ManualTimeProvider.Yerevan(2026, 10, 20, 0, 30).UtcDateTime;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.JobRuns.Add(JobRun.Start(Guid.NewGuid(), runner.JobName, date, started, started.AddHours(1)));
            await db.SaveChangesAsync();
        }

        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 20, 1, 0));
        Assert.False(await runner.TryRunAsync(default));               // the other instance may still be running

        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 20, 1, 31));
        Assert.True(await runner.TryRunAsync(default));                // its lease (1 hour) is over
        Assert.Equal(2, (await fixture.RunRecordAsync(runner.JobName, date)).Attempts);
    }

    [Fact]
    public async Task A_job_working_longer_than_the_lease_keeps_it_and_is_not_started_by_another_instance()
    {
        string name = $"long-{Guid.NewGuid():N}";
        var first = fixture.NewRunner(HalfPastMidnight, name: name);
        var second = fixture.NewRunner(HalfPastMidnight, name: name);
        var date = new DateOnly(2026, 10, 26);
        DateTime start = ManualTimeProvider.Yerevan(2026, 10, 26, 0, 30).UtcDateTime;
        fixture.Time.SetUtcNow(start);
        (TaskCompletionSource gate, Task started) = fixture.Log.LongJob(name);

        Task<bool> running = first.TryRunAsync(default);
        await started;

        // The lease is extended every 20 minutes (a third of 1 hour) while the job works.
        foreach (int minutes in new[] { 25, 50, 75, 100 })
        {
            await JobsFixture.EventuallyAsync(() => fixture.Time.WaitingTimers > 0, "the heartbeat waits for its next beat");
            fixture.Time.SetUtcNow(start.AddMinutes(minutes));
            DateTime expected = start.AddMinutes(minutes + 60);
            await JobsFixture.EventuallyAsync(
                async () => (await fixture.RunRecordAsync(name, date)).LeaseUntilUtc == expected, $"lease extended at +{minutes} min");
        }

        Assert.False(await second.TryRunAsync(default));               // 100 minutes after the start, still its run

        gate.SetResult();
        Assert.True(await running);
        JobRun run = await fixture.RunRecordAsync(name, date);
        Assert.Equal(JobRunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.Attempts);
        Assert.Equal(1, fixture.Log.Runs[name]);
    }

    [Fact]
    public async Task An_old_attempt_cannot_record_its_result_over_the_attempt_that_took_the_run_over()
    {
        string name = $"stale-{Guid.NewGuid():N}";
        var date = new DateOnly(2026, 10, 27);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 27, 0, 30));
        JobRunClaim first = (await fixture.WithStoreAsync(s => s.TryStartAsync(name, date, fixture.Settings, default)))!;

        fixture.Time.Advance(TimeSpan.FromMinutes(61));                // the first instance froze past its lease
        JobRunClaim second = (await fixture.WithStoreAsync(s => s.TryStartAsync(name, date, fixture.Settings, default)))!;
        Assert.Equal(2, second.Attempt);

        Assert.False(await fixture.WithStoreAsync(s => s.SucceedAsync(first, "late", default)));
        Assert.False(await fixture.WithStoreAsync(s => s.FailAsync(first, "late", default)));
        Assert.False(await fixture.WithStoreAsync(s => s.ExtendLeaseAsync(first, fixture.Settings.Lease, default)));
        JobRun untouched = await fixture.RunRecordAsync(name, date);
        Assert.Equal(JobRunStatus.Running, untouched.Status);
        Assert.Equal(2, untouched.Attempts);

        Assert.True(await fixture.WithStoreAsync(s => s.SucceedAsync(second, "done", default)));
        Assert.Equal(JobRunStatus.Succeeded, (await fixture.RunRecordAsync(name, date)).Status);
    }

    [Fact]
    public async Task An_instance_whose_run_was_taken_over_stops_its_job_and_leaves_the_new_attempt_alone()
    {
        string name = $"lost-{Guid.NewGuid():N}";
        var runner = fixture.NewRunner(HalfPastMidnight, name: name);
        var date = new DateOnly(2026, 10, 28);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 28, 0, 30));
        (_, Task started) = fixture.Log.LongJob(name);

        Task<bool> running = runner.TryRunAsync(default);
        await started;

        // Its lease ran out while it could not reach the database, and another instance took the run over.
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().JobRuns
                .Where(r => r.JobName == name)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.LeaseUntilUtc, fixture.Time.GetUtcNow().UtcDateTime));
        JobRunClaim takeover = (await fixture.WithStoreAsync(s => s.TryStartAsync(name, date, fixture.Settings, default)))!;

        await JobsFixture.EventuallyAsync(() => fixture.Time.WaitingTimers > 0, "the heartbeat waits for its next beat");
        fixture.Time.Advance(TimeSpan.FromMinutes(21));                // next beat: the run is no longer its own
        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(10))); // the job was cancelled

        JobRun run = await fixture.RunRecordAsync(name, date);
        Assert.Equal(JobRunStatus.Running, run.Status);                // not marked Failed by the old instance
        Assert.Equal(takeover.Attempt, run.Attempts);
        Assert.Contains(fixture.Logger.For(name), e => e.Level == LogLevel.Warning && e.Message.Contains("taken the run over"));
    }

    [Fact]
    public async Task A_run_stopped_by_the_shutdown_is_recorded_and_tried_again_after_the_retry_delay()
    {
        string name = $"stop-{Guid.NewGuid():N}";
        var runner = fixture.NewRunner(HalfPastMidnight, name: name);
        var date = new DateOnly(2026, 10, 29);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 29, 0, 30));
        (TaskCompletionSource gate, Task started) = fixture.Log.LongJob(name);
        using var shutdown = new CancellationTokenSource();

        Task<bool> running = runner.TryRunAsync(shutdown.Token);
        await started;
        await shutdown.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        JobRun stopped = await fixture.RunRecordAsync(name, date);
        Assert.Equal(JobRunStatus.Failed, stopped.Status);
        Assert.StartsWith("Stopped", stopped.LastError);

        gate.SetResult();
        fixture.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.True(await fixture.NewRunner(HalfPastMidnight, name: name).TryRunAsync(default));   // another instance, after the delay
        Assert.Equal(JobRunStatus.Succeeded, (await fixture.RunRecordAsync(name, date)).Status);
    }

    [Theory]
    [InlineData(2026, 10, 17)]   // Saturday
    [InlineData(2026, 9, 21)]    // Monday, a public holiday from the settings
    public async Task A_working_days_job_does_not_run_on_a_day_off(int year, int month, int day)
    {
        var runner = fixture.NewRunner(new TimeOnly(9, 0), workingDaysOnly: true);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(year, month, day, 9, 30));

        Assert.False(await runner.TryRunAsync(default));
        Assert.False(fixture.Log.Runs.ContainsKey(runner.JobName));
    }

    [Fact]
    public async Task A_working_days_job_runs_on_a_working_day()
    {
        var runner = fixture.NewRunner(new TimeOnly(9, 0), workingDaysOnly: true);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 9, 22, 9, 0));   // Tuesday

        Assert.True(await runner.TryRunAsync(default));
    }

    [Fact]
    public async Task A_job_for_every_day_runs_on_weekends_too()
    {
        var runner = fixture.NewRunner(HalfPastMidnight, workingDaysOnly: false);   // BOR-06: daily, not only working days
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 18, 0, 30));    // Sunday

        Assert.True(await runner.TryRunAsync(default));
    }

    [Fact]
    public async Task The_background_loop_runs_the_job_when_the_clock_reaches_the_set_time_and_stops_with_the_host()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 30, 0, 29));

        await runner.StartAsync(default);
        await JobsFixture.EventuallyAsync(() => fixture.Time.WaitingTimers > 0, "the loop waits for its next check");
        Assert.False(fixture.Log.Runs.ContainsKey(runner.JobName));    // checked at 00:29: not due

        fixture.Time.Advance(TimeSpan.FromMinutes(1));                 // the next check, at 00:30
        await JobsFixture.EventuallyAsync(() => fixture.Log.Runs.ContainsKey(runner.JobName), "the run at 00:30");

        await runner.StopAsync(default);
        Assert.True(runner.ExecuteTask!.IsCompleted);
        Assert.Equal(1, fixture.Log.Runs[runner.JobName]);
    }

    [Fact]
    public async Task The_background_loop_tries_a_failed_run_again_by_itself()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Log.FailFirst[runner.JobName] = true;
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 31, 0, 30));

        await runner.StartAsync(default);
        await JobsFixture.EventuallyAsync(() => fixture.Log.Runs.GetValueOrDefault(runner.JobName) == 1, "the first, failing run");
        // While the run works, its lease heartbeat waits on a timer too. Only after the failure is recorded (the
        // heartbeat is stopped before that) is the waiting timer the loop's next check, which the clock must reach.
        await JobsFixture.EventuallyAsync(
            async () => (await fixture.RunRecordAsync(runner.JobName, new DateOnly(2026, 10, 31))).Status == JobRunStatus.Failed,
            "the failure is recorded");
        await JobsFixture.EventuallyAsync(() => fixture.Time.WaitingTimers > 0, "the loop waits for its next check");

        fixture.Time.Advance(TimeSpan.FromMinutes(16));                // past the retry delay
        await JobsFixture.EventuallyAsync(
            async () => (await fixture.RunRecordAsync(runner.JobName, new DateOnly(2026, 10, 31))).Status == JobRunStatus.Succeeded,
            "the second run succeeded");

        await runner.StopAsync(default);
        Assert.Equal(2, fixture.Log.Runs[runner.JobName]);
    }

    [Theory]
    [InlineData(false, true)]    // Jobs:Enabled = false (test hosts, maintenance)
    [InlineData(true, false)]    // Jobs:Schedules:<name>:Enabled = false
    public async Task A_disabled_job_does_not_start_its_loop(bool jobsEnabled, bool scheduleEnabled)
    {
        var runner = fixture.NewRunner(HalfPastMidnight, jobsEnabled: jobsEnabled, scheduleEnabled: scheduleEnabled);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 11, 1, 10, 0));

        await runner.StartAsync(default);
        await runner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(fixture.Log.Runs.ContainsKey(runner.JobName));
        await runner.StopAsync(default);
    }
}
