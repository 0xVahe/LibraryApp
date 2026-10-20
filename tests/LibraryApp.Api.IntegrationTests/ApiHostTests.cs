using System.Net;
using System.Security.Cryptography;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>
/// Starts the real API host against a throw-away database with all migrations applied.
/// The server comes from <see cref="TestSqlServer"/>, the same as for the infrastructure tests.
/// </summary>
public class ApiHostFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString = TestSqlServer.NewConnectionString("LibraryApp_ApiTests");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Created here, before the application starts: its start-up migrations, InitializeAsync and every test then
        // find the database, so no test host issues its own CREATE DATABASE outside the server-wide creation lock.
        TestSqlServer.CreateDatabase(_connectionString);

        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:LibraryDb", _connectionString);
        builder.UseSetting("UseHttpsRedirection", "false");
        builder.UseSetting("Messaging:DispatcherEnabled", "false");
        builder.UseSetting("Jobs:Enabled", "false");                 // daily jobs never run during API tests
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("LoginRateLimit:PermitLimit", "1000");
        builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(Logs));
        builder.ConfigureServices(ConfigureTestServices);
    }

    /// <summary>
    /// Log events of every test host. Each host writes to its own Serilog logger (preserveStaticLogger in Program.cs),
    /// and every host has this sink, so a test finds the events of its own requests.
    /// </summary>
    public static CollectingSink Logs { get; } = new();

    /// <summary>Extra services of a test class (test controllers, validators).</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services) { }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database;
        EnsureOwnDatabase(database);
        await database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database;
            EnsureOwnDatabase(database);
            await database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>
    /// The host must use the throw-away database of this factory. If a setting from the environment won (e.g.
    /// ConnectionStrings__LibraryDb), stop before a developer database is migrated or dropped.
    /// </summary>
    private void EnsureOwnDatabase(DatabaseFacade database)
    {
        var expected = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
        var actual = new SqlConnectionStringBuilder(database.GetConnectionString()).InitialCatalog;
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The test host uses database '{actual}' instead of its own '{expected}'; refusing to migrate or delete it.");
    }
}

public class ApiHostTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Endpoint_answers_200(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class CollectingSink : ILogEventSink
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
