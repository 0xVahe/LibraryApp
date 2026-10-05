using Microsoft.Data.SqlClient;

namespace LibraryApp.Infrastructure.IntegrationTests;

/// <summary>
/// Where the tests that need a real SQL Server create their database.
/// CI sets <see cref="ServerVariable"/> (a SQL Server container); on a Windows developer machine the default is LocalDB.
/// Every fixture gets its own database name, so test classes running in parallel never share or delete each
/// other's database; the fixture deletes it at the end.
/// </summary>
internal static class TestDatabase
{
    public const string ServerVariable = "LIBRARYAPP_TEST_DB";

    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static string NewConnectionString()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);

        return new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(server) ? LocalDb : server)
        {
            InitialCatalog = $"LibraryApp_Tests_{Guid.NewGuid():N}",
        }.ConnectionString;
    }
}
