using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Jobs;

/// <summary>
/// Settings section <c>Jobs</c> (SD-10, SD-21): how the platform runs daily jobs, and the schedule of each job. Values
/// live in <c>appsettings.json</c>; <see cref="JobsOptionsValidator"/> stops the application at start when one is missing.
/// </summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Master switch: false stops every job of this instance (test hosts, maintenance).</summary>
    public bool Enabled { get; set; }

    /// <summary>How often an instance checks whether a run is due; also the catch-up delay after a start.</summary>
    public TimeSpan PollInterval { get; set; }

    /// <summary>A Running record older than this belongs to an instance that stopped; another instance takes the run over.</summary>
    public TimeSpan Lease { get; set; }

    /// <summary>Wait after a failed run before the next attempt.</summary>
    public TimeSpan RetryDelay { get; set; }

    /// <summary>Attempts per job and local day, failures included.</summary>
    public int MaxAttemptsPerDay { get; set; }

    /// <summary>One entry per registered job, keyed by its name.</summary>
    public Dictionary<string, JobScheduleOptions> Schedules { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Section <c>Jobs:Schedules:&lt;name&gt;</c> — when one job runs.</summary>
public sealed class JobScheduleOptions
{
    public bool? Enabled { get; set; }

    /// <summary>Asia/Yerevan time of day from which the run of the day is due.</summary>
    public TimeOnly? RunAt { get; set; }

    /// <summary>True: no run on weekends and public holidays (the shared working-day calendar, BR-BOR-5).</summary>
    public bool? WorkingDaysOnly { get; set; }
}

/// <summary>A job registered with <c>AddDailyJob</c>: its name and the <c>IDailyJob</c> type that does the work.</summary>
public sealed record DailyJobRegistration(string Name, Type JobType);

/// <summary>
/// Checks the <c>Jobs</c> section at start: positive intervals and attempts, and a complete schedule for every
/// registered job (a job without a schedule would silently never run).
/// </summary>
public sealed class JobsOptionsValidator(IEnumerable<DailyJobRegistration> registrations) : IValidateOptions<JobsOptions>
{
    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        var failures = new List<string>();
        const string section = JobsOptions.SectionName;

        if (options.PollInterval <= TimeSpan.Zero)
            failures.Add($"{section}:{nameof(JobsOptions.PollInterval)} must be positive (found {options.PollInterval}).");
        if (options.Lease <= TimeSpan.Zero)
            failures.Add($"{section}:{nameof(JobsOptions.Lease)} must be positive (found {options.Lease}).");
        if (options.RetryDelay < TimeSpan.Zero)
            failures.Add($"{section}:{nameof(JobsOptions.RetryDelay)} must not be negative (found {options.RetryDelay}).");
        if (options.MaxAttemptsPerDay < 1)
            failures.Add($"{section}:{nameof(JobsOptions.MaxAttemptsPerDay)} must be 1 or more (found {options.MaxAttemptsPerDay}).");

        foreach (DailyJobRegistration job in registrations)
        {
            string key = $"{section}:{nameof(JobsOptions.Schedules)}:{job.Name}";
            if (!options.Schedules.TryGetValue(job.Name, out JobScheduleOptions? schedule))
            {
                failures.Add($"{key} is missing: set Enabled, RunAt and WorkingDaysOnly for the job.");
                continue;
            }

            if (schedule.Enabled is null)
                failures.Add($"{key}:{nameof(JobScheduleOptions.Enabled)} is missing.");
            if (schedule.RunAt is null)
                failures.Add($"{key}:{nameof(JobScheduleOptions.RunAt)} is missing (local time, for example \"00:30\").");
            if (schedule.WorkingDaysOnly is null)
                failures.Add($"{key}:{nameof(JobScheduleOptions.WorkingDaysOnly)} is missing.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
