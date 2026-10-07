using LibraryApp.Application.Abstractions.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibraryApp.Infrastructure.Persistence.Seed;

/// <summary>
/// Runs at application start: applies migrations (<c>Database:MigrateOnStartup</c>), then the module seeders in order
/// (<c>Seed:Enabled</c>). Seed data is applied ONLY in the Development environment (test accounts must never reach
/// other environments). Every seeder must be idempotent.
/// </summary>
public sealed class DatabaseInitializer(
    LibraryDbContext db,
    IUnitOfWork unitOfWork,
    IEnumerable<IModuleSeeder> seeders,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (configuration.GetValue("Database:MigrateOnStartup", false))
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);
        }

        if (!configuration.GetValue("Seed:Enabled", false))
            return;

        if (!environment.IsDevelopment())
        {
            logger.LogWarning("Seed:Enabled is set, but seed data is applied only in the Development environment. Skipped.");
            return;
        }

        foreach (var seeder in seeders.OrderBy(s => s.Order))
        {
            logger.LogInformation("Seeding module {Module}", seeder.Module);
            await unitOfWork.ExecuteInTransactionAsync(token => seeder.SeedAsync(token), ct);
        }
    }
}
