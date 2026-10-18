using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Jobs;

/// <summary>A module registers its job with AddDailyJob; the "Jobs" section is checked at start (SD-10, SD-21).</summary>
public class DailyJobRegistrationTests
{
    private const string JobName = "sample-daily";

    private static readonly Dictionary<string, string?> Valid = new()
    {
        ["Jobs:Enabled"] = "true",
        ["Jobs:PollInterval"] = "00:01:00",
        ["Jobs:Lease"] = "01:00:00",
        ["Jobs:RetryDelay"] = "00:15:00",
        ["Jobs:MaxAttemptsPerDay"] = "5",
        [$"Jobs:Schedules:{JobName}:Enabled"] = "true",
        [$"Jobs:Schedules:{JobName}:RunAt"] = "00:30",
        [$"Jobs:Schedules:{JobName}:WorkingDaysOnly"] = "false",
    };

    private static ServiceProvider Services(Dictionary<string, string?> settings, bool withJob = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddDailyJobs(configuration);
        if (withJob)
            services.AddDailyJob<SampleJob>(JobName);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_section_and_the_schedule_are_bound()
    {
        using var services = Services(Valid);

        JobsOptions options = services.GetRequiredService<IOptions<JobsOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(15), options.RetryDelay);
        Assert.Equal(new TimeOnly(0, 30), options.Schedules[JobName].RunAt);
        Assert.False(options.Schedules[JobName].WorkingDaysOnly);
    }

    [Fact]
    public void A_registered_job_gets_its_own_background_runner()
    {
        var services = new ServiceCollection();
        services.AddDailyJob<SampleJob>(JobName);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Contains(services, d => d.ImplementationInstance is DailyJobRegistration { Name: JobName });
        Assert.Contains(services, d => d.ServiceType == typeof(SampleJob) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void The_same_name_twice_is_refused()
    {
        var services = new ServiceCollection();
        services.AddDailyJob<SampleJob>(JobName);

        Assert.Throws<InvalidOperationException>(() => services.AddDailyJob<SampleJob>(JobName));
    }

    [Fact]
    public void A_job_without_a_schedule_fails_at_start()
    {
        var settings = Valid.Where(pair => !pair.Key.StartsWith("Jobs:Schedules", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        using var services = Services(settings);

        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobsOptions>>().Value);
        Assert.Contains($"Jobs:Schedules:{JobName} is missing", ex.Message);
    }

    [Theory]
    [InlineData("RunAt")]
    [InlineData("Enabled")]
    [InlineData("WorkingDaysOnly")]
    public void A_schedule_without_a_value_fails_at_start(string key)
    {
        var settings = new Dictionary<string, string?>(Valid);
        settings.Remove($"Jobs:Schedules:{JobName}:{key}");
        using var services = Services(settings);

        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobsOptions>>().Value);
        Assert.Contains($"Jobs:Schedules:{JobName}:{key} is missing", ex.Message);
    }

    [Theory]
    [InlineData("Jobs:PollInterval", "00:00:00")]
    [InlineData("Jobs:Lease", "00:00:00")]
    [InlineData("Jobs:MaxAttemptsPerDay", "0")]
    public void Invalid_runner_settings_fail_at_start(string key, string value)
    {
        using var services = Services(new(Valid) { [key] = value }, withJob: false);

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobsOptions>>().Value);
    }

    private sealed class SampleJob : IDailyJob
    {
        public Task<string> RunAsync(DailyJobRun run, CancellationToken ct) => Task.FromResult("done");
    }
}
