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
/// CI sets LIBRARYAPP_TEST_DB (SQL Server container); on a Windows developer machine LocalDB is used.
/// </summary>
public class ApiHostFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _connectionString = new SqlConnectionStringBuilder(
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIBRARYAPP_TEST_DB"))
            ? LocalDb
            : Environment.GetEnvironmentVariable("LIBRARYAPP_TEST_DB"))
    {
        InitialCatalog = $"LibraryApp_ApiTests_{Guid.NewGuid():N}",
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:LibraryDb", _connectionString);
        builder.UseSetting("UseHttpsRedirection", "false");
        builder.UseSetting("Messaging:DispatcherEnabled", "false");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("LoginRateLimit:PermitLimit", "1000");
        builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(Logs));
        builder.ConfigureServices(ConfigureTestServices);
    }

    /// <summary>
    /// Log events of every test host. Shared, because application loggers of Serilog may still write through the
    /// static Log.Logger, which the host built last replaces; every host has this sink, so no event is lost.
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
