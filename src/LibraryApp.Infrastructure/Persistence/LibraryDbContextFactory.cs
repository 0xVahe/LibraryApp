using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LibraryApp.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tools (<c>dotnet ef migrations add</c>, <c>has-pending-model-changes</c>), so they work
/// without starting the API and without any secret: these two commands never connect to the database.
/// To run <c>dotnet ef database update</c> against another server, set the environment variable
/// <see cref="ConnectionStringVariable"/> or put it in the repo-root <c>.env</c> (see <c>.env.sample</c>).
/// </summary>
public sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public const string ConnectionStringVariable = "LIBRARYAPP_DESIGNTIME_DB";

    private const string DefaultConnection =
        @"Server=(localdb)\MSSQLLocalDB;Database=LibraryApp;Trusted_Connection=True;TrustServerCertificate=True";

    public LibraryDbContext CreateDbContext(string[] args)
    {
        // Design-time only (dotnet ef): loads the repo-root .env. Variables already set (CI, shell) win; no .env changes nothing.
        DotNetEnv.Env.TraversePath().NoClobber().Load();

        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = DefaultConnection;

        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(connectionString);

        return new LibraryDbContext(options.Options);
    }
}
