using System.Net;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        builder.ConfigureServices(ConfigureTestServices);
    }

    /// <summary>Extra services of a test class (test controllers, validators).</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services) { }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();

        await base.DisposeAsync();
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
