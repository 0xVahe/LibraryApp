using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace LibraryApp.Tests.Shared;

/// <summary>
/// The SQL Server of every integration test, decided in one place (linked into each test project). Order:
/// <list type="number">
/// <item>the environment variable <see cref="Variable"/> (CI, a shell, the test runner settings of the IDE);</item>
/// <item><see cref="Variable"/> in the repo-root <c>.env</c> (local development with docker compose); only this one
/// value is read, nothing else from <c>.env</c> reaches the test process;</item>
/// <item>LocalDB, when neither is set (Windows only).</item>
/// </list>
/// Before the first test database is created, the server is checked once (no retries). If it does not answer, every
/// test that needs a database fails at once with the same message: which server, where the setting came from and what
/// to do. At that point test databases left by interrupted runs (older than <see cref="StaleAfter"/>) are deleted.
/// </summary>
internal static partial class TestSqlServer
{
    public const string Variable = "LIBRARYAPP_TEST_DB";

    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    private static readonly Lazy<(string Server, string Source)> Setting =
        new(() => Resolve(Environment.GetEnvironmentVariable(Variable), FindDotEnv()));

    private static readonly Lazy<string?> Failure = new(() => Check(Setting.Value.Server, Setting.Value.Source));

    /// <summary>
    /// Name of the server-wide application lock every test database creation takes (see <see cref="CreateDatabase"/>).
    /// </summary>
    internal const string CreateLockResource = "LibraryApp.Tests.CreateDatabase";

    /// <summary>How long one creation waits for the creations before it.</summary>
    private static readonly TimeSpan CreateLockTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A connection string to a new database on the test server that does not exist yet. Only for the code that
    /// creates it through <see cref="CreateDatabase"/> (<see cref="NewDatabase"/>, the API test host) and for a test
    /// that checks a database is not created; every other test uses <see cref="NewDatabase"/>.
    /// </summary>
    /// <param name="prefix">Start of the database name, for example <c>LibraryApp_Tests</c>.</param>
    public static string NewConnectionString(string prefix)
    {
        if (Failure.Value is { } message)
            throw new InvalidOperationException(message);

        return new SqlConnectionStringBuilder(Setting.Value.Server)
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    /// <summary>
    /// A new, empty database on the test server, created by <see cref="CreateDatabase"/>; returns its connection string.
    /// The caller fills it (migrations or <c>EnsureCreated</c>) and deletes it.
    /// </summary>
    /// <param name="prefix">Start of the database name, for example <c>LibraryApp_Tests</c>.</param>
    public static string NewDatabase(string prefix)
    {
        var connectionString = NewConnectionString(prefix);
        CreateDatabase(connectionString);
        return connectionString;
    }

    /// <summary>
    /// Creates the empty database of <paramref name="connectionString"/> unless it exists. SQL Server copies the
    /// <c>model</c> database for every <c>CREATE DATABASE</c> and needs an exclusive lock on it; two creations at the same
    /// moment do not wait for each other, one of them fails with error 1807 ("Could not obtain exclusive lock on database
    /// 'model'"). Test classes run in parallel and several test processes may use one server (<c>dotnet test</c> of the
    /// solution), so every creation first takes one server-wide application lock (<see cref="CreateLockResource"/>):
    /// the creations of all test processes on this server run one after another. Migrations and <c>EnsureCreated</c>
    /// then find the database and only create its tables, which needs no lock on <c>model</c>.
    /// </summary>
    public static void CreateDatabase(string connectionString)
    {
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!TestDatabaseName().IsMatch(name))
            throw new ArgumentException($"'{name}' is not a test database name; refusing to create it.", nameof(connectionString));

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false };
        using var connection = new SqlConnection(master.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandTimeout = (int)CreateLockTimeout.TotalSeconds + 60;
        // The name matches TestDatabaseName (a prefix and 32 hex digits), so it is safe inside the brackets.
        // The lock belongs to the session: closing the connection (no pooling) releases it, also after an error.
        command.CommandText = $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = @timeout;
            IF @result < 0
                THROW 50000, N'The test database creation lock was not granted in time.', 1;
            IF DB_ID(@name) IS NULL
                CREATE DATABASE [{name}];
            EXEC sp_releaseapplock @Resource = @resource, @LockOwner = N'Session';
            """;
        command.Parameters.AddWithValue("@resource", CreateLockResource);
        command.Parameters.AddWithValue("@timeout", (int)CreateLockTimeout.TotalMilliseconds);
        command.Parameters.AddWithValue("@name", name);
        command.ExecuteNonQuery();
    }

    /// <summary>The order of the class summary: environment, then <paramref name="dotEnvPath"/>, then LocalDB.</summary>
    internal static (string Server, string Source) Resolve(string? fromEnvironment, string? dotEnvPath)
    {
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return (fromEnvironment, $"environment variable {Variable}");

        if (dotEnvPath is { } path)
        {
            string? fromFile = null;
            foreach (var pair in DotNetEnv.Env.NoEnvVars().Load(path))
                if (pair.Key == Variable)
                    fromFile = pair.Value;

            if (!string.IsNullOrWhiteSpace(fromFile))
                return (fromFile, $"{Variable} in {path}");
        }

        return (LocalDb, $"LocalDB: {Variable} is set neither in the environment nor in .env");
    }

    private static string? FindDotEnv()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Null when the server answers; otherwise the message every database test fails with.</summary>
    internal static string? Check(string server, string source)
    {
        var builder = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = "master",
            ConnectTimeout = server.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ? 30 : 5,   // LocalDB starts on the first connection
            ConnectRetryCount = 0,
            Pooling = false,
        };

        try
        {
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            DeleteStaleDatabases(connection);
            return null;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            return $"""
                The integration tests cannot reach SQL Server '{builder.DataSource}' (from {source}).
                {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}
                What to do:
                - SQL Server in Docker: start Docker Desktop, run `docker compose up -d sqlserver` in the repository root,
                  and set {Variable} in .env (see .env.sample and the README, "Tests fail all at once").
                - An environment variable {Variable} wins over .env: remove it if it points to another server.
                - LocalDB is used only when {Variable} is set nowhere: check it with `sqllocaldb info MSSQLLocalDB`.
                """;
        }
    }

    /// <summary>Test databases whose run was interrupted (the fixture never deleted them). Best effort.</summary>
    private static void DeleteStaleDatabases(SqlConnection connection)
    {
        var stale = new List<string>();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT name FROM sys.databases WHERE name LIKE 'LibraryApp[_]%Tests[_]%' AND create_date < DATEADD(SECOND, -@age, GETDATE())";
            query.Parameters.AddWithValue("@age", (int)StaleAfter.TotalSeconds);
            using var reader = query.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(0) is var name && TestDatabaseName().IsMatch(name))
                    stale.Add(name);
        }

        foreach (var name in stale)
        {
            try
            {
                using var drop = connection.CreateCommand();
                drop.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
                drop.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                // Another run may be deleting it, or its files are gone; it does not affect this run.
            }
        }
    }

    /// <summary>Only names this class creates: a known prefix and a 32-digit id.</summary>
    [GeneratedRegex("^LibraryApp_(Tests|ApiTests)_[0-9a-f]{32}$")]
    private static partial Regex TestDatabaseName();
}
