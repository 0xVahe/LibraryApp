using System.Collections.Concurrent;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.IntegrationTests.Time;
using LibraryApp.Infrastructure.Jobs;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Jobs;

/// <summary>The platform as the API registers it, with a clock the tests move, on a throw-away database.</summary>
public sealed class JobsFixture : IAsyncLifetime
{
    public ManualTimeProvider Time { get; } = new(ManualTimeProvider.Yerevan(2026, 10, 12, 10, 0));

    public ServiceProvider Services { get; private set; } = null!;

    public JobLog Log => Services.GetRequiredService<JobLog>();

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:LibraryDb"] = TestDatabase.NewConnectionString(),
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

    /// <summary>A runner for a new job with its own name and schedule, as AddDailyJob would create it.</summary>
    public DailyJobRunner NewRunner(TimeOnly runAt, bool workingDaysOnly = false, string? name = null)
    {
        name ??= $"test-{Guid.NewGuid():N}";
        var options = Services.GetRequiredService<IOptions<JobsOptions>>();
        options.Value.Schedules[name] = new JobScheduleOptions { Enabled = true, RunAt = runAt, WorkingDaysOnly = workingDaysOnly };
        return new DailyJobRunner(
            new DailyJobRegistration(name, typeof(CountingJob)),
            Services.GetRequiredService<IServiceScopeFactory>(),
            Services.GetRequiredService<IClock>(),
            Services.GetRequiredService<IWorkingDayCalendar>(),
            options,
            Time,
            NullLogger<DailyJobRunner>.Instance);
    }

    public async Task<JobRun> RunRecordAsync(string jobName, DateOnly date)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().JobRuns.AsNoTracking()
            .SingleAsync(r => r.JobName == jobName && r.LocalDate == date);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LibraryApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>What the test jobs did: runs per job name, and the jobs whose first run fails.</summary>
public sealed class JobLog
{
    public ConcurrentDictionary<string, int> Runs { get; } = new();

    public ConcurrentDictionary<string, DailyJobRun> LastRun { get; } = new();

    public ConcurrentDictionary<string, bool> FailFirst { get; } = new();
}

public sealed class CountingJob(JobLog log) : IDailyJob
{
    public Task<string> RunAsync(DailyJobRun run, CancellationToken ct)
    {
        int count = log.Runs.AddOrUpdate(run.JobName, 1, (_, runs) => runs + 1);
        log.LastRun[run.JobName] = run;
        if (count == 1 && log.FailFirst.ContainsKey(run.JobName))
            throw new InvalidOperationException("boom");
        return Task.FromResult($"ran {count}");
    }
}

/// <summary>SD-10: a daily job runs once per local day across instances, catches up after downtime and retries a failure.</summary>
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
    public async Task An_instance_that_was_down_at_the_set_time_catches_up_when_it_starts()
    {
        var runner = fixture.NewRunner(HalfPastMidnight);
        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 15, 15, 0));   // started in the afternoon

        Assert.True(await runner.TryRunAsync(default));
    }

    [Fact]
    public async Task A_failed_run_is_tried_again_after_the_retry_delay_up_to_the_daily_attempts()
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
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.JobRuns.Add(JobRun.Start(Guid.NewGuid(), runner.JobName, date, ManualTimeProvider.Yerevan(2026, 10, 20, 0, 30).UtcDateTime));
            await db.SaveChangesAsync();
        }

        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 20, 1, 0));
        Assert.False(await runner.TryRunAsync(default));               // the other instance may still be running

        fixture.Time.SetUtcNow(ManualTimeProvider.Yerevan(2026, 10, 20, 1, 31));
        Assert.True(await runner.TryRunAsync(default));                // its lease (1 hour) is over
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
}
