using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The start-up runner on a real SQL Server: migrations only when Database:MigrateOnStartup is true; seeders only when
/// Seed:Enabled is true AND the environment is Development; seeders in Order. Each test uses its own database.
/// </summary>
public class DatabaseInitializerTests
{
    [Fact]
    public async Task Applies_all_migrations_when_MigrateOnStartup_is_true()
    {
        await using var db = NewDatabase();
        try
        {
            await Initializer(db, migrate: true, seed: false, Environments.Development).InitializeAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Does_not_touch_the_database_when_MigrateOnStartup_is_false()
    {
        await using var db = NewDatabase(created: false);

        await Initializer(db, migrate: false, seed: false, Environments.Development).InitializeAsync();

        Assert.False(await db.Database.CanConnectAsync());          // the database was not even created
    }

    [Fact]
    public async Task Runs_the_seeders_in_order_in_Development()
    {
        await using var db = NewDatabase();
        var log = new List<string>();
        try
        {
            await Initializer(db, migrate: true, seed: true, Environments.Development,
                new RecordingSeeder(20, "Staff", log), new RecordingSeeder(10, "Catalog", log)).InitializeAsync();

            Assert.Equal(["Catalog", "Staff"], log);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Never_seeds_outside_Development_even_when_Seed_is_enabled(string environment)
    {
        await using var db = NewDatabase();
        var log = new List<string>();
        try
        {
            await Initializer(db, migrate: true, seed: true, environment, new RecordingSeeder(10, "Catalog", log)).InitializeAsync();

            Assert.Empty(log);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Does_not_seed_when_Seed_is_disabled()
    {
        await using var db = NewDatabase();
        var log = new List<string>();
        try
        {
            await Initializer(db, migrate: true, seed: false, Environments.Development, new RecordingSeeder(10, "Catalog", log)).InitializeAsync();

            Assert.Empty(log);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>
    /// A context on its own database: created empty (the initializer applies the migrations to it), or not created at
    /// all for the test that checks the initializer leaves the server alone.
    /// </summary>
    private static LibraryDbContext NewDatabase(bool created = true)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(created ? TestDatabase.NewDatabase() : TestDatabase.NewConnectionString());
        return new LibraryDbContext(options.Options);
    }

    private static DatabaseInitializer Initializer(
        LibraryDbContext db, bool migrate, bool seed, string environment, params IModuleSeeder[] seeders)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = migrate.ToString(),
            ["Seed:Enabled"] = seed.ToString(),
        }).Build();

        return new DatabaseInitializer(db, new UnitOfWork(db), seeders, configuration,
            new FakeEnvironment(environment), NullLogger<DatabaseInitializer>.Instance);
    }

    private sealed class RecordingSeeder(int order, string module, List<string> log) : IModuleSeeder
    {
        public int Order => order;

        public string Module => module;

        public Task SeedAsync(CancellationToken ct)
        {
            log.Add(module);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "LibraryApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
