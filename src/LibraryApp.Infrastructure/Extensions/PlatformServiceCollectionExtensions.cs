using FluentValidation;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed;
using LibraryApp.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Extensions;

public static class PlatformServiceCollectionExtensions
{
    public const string ConnectionStringName = "LibraryDb";

    /// <summary>
    /// Registers what every module shares: the database context (with retry), the unit of work, the clock, the id
    /// generator and the SQL Server health check. Called once from Program.cs; modules register their own services
    /// in their own Add&lt;Module&gt; methods. Later platform items (error handler, event bus, seed runner) add here.
    /// </summary>
    public static IServiceCollection AddPlatform(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set ConnectionStrings:{ConnectionStringName} " +
                $"(appsettings.Development.json, user-secrets or the environment variable ConnectionStrings__{ConnectionStringName}).");

        // Same SQL Server options as the EF tools (LibraryDbContextFactory): retry on transient failures, migrations assembly.
        services.AddDbContext<LibraryDbContext>(options => options
            .UseLibrarySqlServer(connectionString)
            .EnableSensitiveDataLogging(environment.IsDevelopment()));

        services.Configure<TimeZoneOptions>(configuration.GetSection(TimeZoneOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();

        // Working days for every module (BR-BOR-5, BQ-G4-05); the holiday list is checked at start.
        services.AddOptions<HolidayOptions>().Bind(configuration.GetSection(HolidayOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<HolidayOptions>, HolidayOptionsValidator>();
        services.AddSingleton<IWorkingDayCalendar, WorkingDayCalendar>();

        services.AddSingleton<IIdGenerator, SequentialGuidGenerator>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IApplicationLock, SqlServerApplicationLock>();

        // Validators of all modules (SD-15): every AbstractValidator<T> in the Application assembly.
        services.AddValidatorsFromAssembly(typeof(CommonErrors).Assembly, includeInternalTypes: true);

        services.AddEventBus(configuration);

        // Daily jobs of the modules run once per local day across instances (SD-10); schedules in "Jobs".
        services.AddDailyJobs(configuration);

        services.AddScoped<DatabaseInitializer>();

        services.AddHealthChecks()
            .AddDbContextCheck<LibraryDbContext>("sql-server", tags: ["ready"])
            .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"]);

        return services;
    }
}
