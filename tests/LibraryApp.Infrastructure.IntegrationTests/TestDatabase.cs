namespace LibraryApp.Infrastructure.IntegrationTests;

/// <summary>
/// Where the tests that need a real SQL Server create their database. The server comes from
/// <see cref="TestSqlServer"/> (environment variable, then <c>.env</c>, then LocalDB), the same as for the API tests.
/// Every fixture gets its own database name, so test classes running in parallel never share or delete each
/// other's database; the fixture deletes it at the end.
/// </summary>
internal static class TestDatabase
{
    public static string NewConnectionString() => TestSqlServer.NewConnectionString("LibraryApp_Tests");
}
