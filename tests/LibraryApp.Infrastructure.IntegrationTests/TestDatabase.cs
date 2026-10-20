namespace LibraryApp.Infrastructure.IntegrationTests;

/// <summary>
/// Where the tests that need a real SQL Server create their database. The server comes from
/// <see cref="TestSqlServer"/> (environment variable, then <c>.env</c>, then LocalDB), the same as for the API tests.
/// Every fixture gets its own database, so test classes running in parallel never share or delete each other's
/// database; the fixture deletes it at the end.
/// </summary>
internal static class TestDatabase
{
    private const string Prefix = "LibraryApp_Tests";

    /// <summary>
    /// A new, empty database, already created under the server-wide creation lock of
    /// <see cref="TestSqlServer.CreateDatabase"/>; migrations or <c>EnsureCreated</c> only add its tables.
    /// </summary>
    public static string NewDatabase() => TestSqlServer.NewDatabase(Prefix);

    /// <summary>
    /// The connection string of a database that is not created. Only for a test that checks the database is never
    /// created; anything that creates it must use <see cref="NewDatabase"/> (guarded by <c>TestSqlServerTests</c>).
    /// </summary>
    public static string NewConnectionString() => TestSqlServer.NewConnectionString(Prefix);
}
