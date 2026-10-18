using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Extensions;

public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// The daily job runner of the platform (SD-10): the <c>Jobs</c> settings, checked at start, and the run records.
    /// Called by <c>AddPlatform</c>; modules add their jobs with <see cref="AddDailyJob{TJob}"/>.
    /// </summary>
    public static IServiceCollection AddDailyJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>();
        services.AddScoped<JobRunStore>();
        return services;
    }

    /// <summary>
    /// Runs <typeparamref name="TJob"/> once per local day at the time set in <c>Jobs:Schedules:&lt;name&gt;</c>. Call it from
    /// the module's <c>Add&lt;Module&gt;Module</c>, for example
    /// <c>services.AddDailyJob&lt;BorrowingDailyJob&gt;(BorrowingDailyJob.Name)</c>, and add the schedule to appsettings.json.
    /// The name is stored with every run: never change it once released.
    /// </summary>
    public static IServiceCollection AddDailyJob<TJob>(this IServiceCollection services, string name)
        where TJob : class, IDailyJob
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > JobRun.JobNameMaxLength)
            throw new ArgumentException($"A job name has at most {JobRun.JobNameMaxLength} characters.", nameof(name));
        if (services.Any(d => d.ImplementationInstance is DailyJobRegistration registered && registered.Name == name))
            throw new InvalidOperationException($"A daily job named '{name}' is already registered.");

        var registration = new DailyJobRegistration(name, typeof(TJob));
        services.AddSingleton(registration);
        services.AddScoped<TJob>();
        services.AddSingleton<IHostedService>(provider => ActivatorUtilities.CreateInstance<DailyJobRunner>(provider, registration));
        return services;
    }
}
