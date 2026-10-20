using Microsoft.Data.SqlClient;

namespace LibraryApp.Infrastructure.IntegrationTests;

/// <summary>
/// The rules of <see cref="TestSqlServer"/>: one order for every developer and CI, one clear message when the server
/// is not there, and no test that picks its server on its own.
/// </summary>
public class TestSqlServerTests
{
    private const string Docker = "Server=localhost,1433;User Id=sa;Password=Pw_Check_123;TrustServerCertificate=True";

    [Fact]
    public void The_environment_variable_wins_over_env_file()
    {
        using var file = DotEnv($"{TestSqlServer.Variable}=Server=from-file;TrustServerCertificate=True");

        var (server, source) = TestSqlServer.Resolve(Docker, file.Path);

        Assert.Equal(Docker, server);
        Assert.Contains("environment variable", source);
    }

    [Fact]
    public void Env_file_is_used_when_the_variable_is_not_set_and_its_references_are_expanded()
    {
        // Own names: DotNetEnv expands ${...} from the process first, and another test (the EF design-time factory)
        // loads the real .env into this process, so MSSQL_PORT may already be there.
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var file = DotEnv(
            $"PASSWORD_{suffix}=Pw_Check_123",
            $"PORT_{suffix}=14330",
            $"{TestSqlServer.Variable}=\"Server=localhost,${{PORT_{suffix}}};User Id=sa;Password=${{PASSWORD_{suffix}}};TrustServerCertificate=True\"");

        var (server, source) = TestSqlServer.Resolve(null, file.Path);

        Assert.Equal("Server=localhost,14330;User Id=sa;Password=Pw_Check_123;TrustServerCertificate=True", server);
        Assert.Contains(file.Path, source);
    }

    [Fact]
    public void Only_the_test_server_value_is_read_from_env_file()
    {
        using var file = DotEnv("TESTSQLSERVER_PROBE=leaks", $"{TestSqlServer.Variable}={Docker}");

        TestSqlServer.Resolve(null, file.Path);

        Assert.Null(Environment.GetEnvironmentVariable("TESTSQLSERVER_PROBE"));
    }

    [Fact]
    public void LocalDB_is_the_last_choice_and_says_why()
    {
        using var file = DotEnv("MSSQL_PORT=1433");

        var (server, source) = TestSqlServer.Resolve("  ", file.Path);

        Assert.Contains("MSSQLLocalDB", server);
        Assert.Contains("set neither in the environment nor in .env", source);
    }

    [Fact]
    public void An_unreachable_server_gives_one_message_with_the_server_its_source_and_what_to_do()
    {
        var message = TestSqlServer.Check("Server=127.0.0.1,1;User Id=sa;Password=x;TrustServerCertificate=True", "a test source");

        Assert.NotNull(message);
        Assert.Contains("127.0.0.1,1", message);
        Assert.Contains("a test source", message);
        Assert.Contains("docker compose up -d sqlserver", message);
    }

    /// <summary>
    /// Every test database must come from TestDatabase / ApiHostFactory (that is, from TestSqlServer). A test that
    /// builds its own server address would ignore .env and the check, and fail in the old confusing way.
    /// </summary>
    [Fact]
    public void No_test_chooses_its_SQL_Server_on_its_own()
    {
        var tests = Path.Combine(RepositoryRoot(), "tests");
        string[] forbidden = ["(local" + "db)", "LIBRARYAPP_" + "TEST_DB"];

        var offenders = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnder(path, Path.Combine(tests, "Shared")) && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => forbidden.Any(text => File.ReadAllText(path).Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(tests, path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Use TestDatabase.NewDatabase() or ApiHostFactory instead of choosing a server in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// A connection string to a database that is not created yet may only be taken where the creation is in the
    /// owner's hands: everything else uses <c>NewDatabase</c>, so no test lets EF Core issue its own CREATE DATABASE
    /// outside the creation lock (error 1807 when two creations meet).
    /// </summary>
    [Fact]
    public void Only_the_creation_code_takes_a_database_that_is_not_created()
    {
        var tests = Path.Combine(RepositoryRoot(), "tests");
        string[] allowed =
        [
            Path.Combine("Shared", "TestSqlServer.cs"),
            Path.Combine("LibraryApp.Infrastructure.IntegrationTests", "TestDatabase.cs"),
            Path.Combine("LibraryApp.Infrastructure.IntegrationTests", "TestSqlServerTests.cs"),
            Path.Combine("LibraryApp.Infrastructure.IntegrationTests", "Persistence", "DatabaseInitializerTests.cs"),
            Path.Combine("LibraryApp.Api.IntegrationTests", "ApiHostTests.cs"),
        ];

        var offenders = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains("NewConnectionString(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(tests, path))
            .Where(path => !allowed.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Use TestDatabase.NewDatabase() or ApiHostFactory (the database is created under the creation lock) in: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public async Task A_creation_waits_while_another_session_holds_the_creation_lock()
    {
        var connectionString = TestDatabase.NewConnectionString();
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        Task? creation = null;
        try
        {
            await using var holder = await OpenMasterAsync(connectionString);
            // Other test processes may be creating databases right now: wait for them like any creation does.
            await ExecuteAsync(holder, """
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 120000;
                IF @result < 0
                    THROW 50000, N'The creation lock was not granted to the test.', 1;
                """);

            creation = Task.Run(() => TestSqlServer.CreateDatabase(connectionString));
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.False(creation.IsCompleted);
            Assert.False(await ExistsAsync(connectionString, name));

            await ExecuteAsync(holder, "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = N'Session'");
            await creation.WaitAsync(TimeSpan.FromSeconds(60));

            Assert.True(await ExistsAsync(connectionString, name));
        }
        finally
        {
            // A failed assertion must not leave the waiting creation to make the database after it was dropped.
            if (creation is not null)
                await creation.WaitAsync(TimeSpan.FromMinutes(3)).ContinueWith(_ => { }, TaskScheduler.Default);
            await DropAsync(connectionString, name);
        }
    }

    [Fact]
    public async Task Parallel_creations_all_succeed()
    {
        var connectionStrings = Enumerable.Range(0, 24).Select(_ => TestDatabase.NewConnectionString()).ToList();
        try
        {
            await Task.WhenAll(connectionStrings.Select(connectionString =>
                Task.Run(() => TestSqlServer.CreateDatabase(connectionString))));

            foreach (var connectionString in connectionStrings)
                Assert.True(await ExistsAsync(connectionString, new SqlConnectionStringBuilder(connectionString).InitialCatalog));
        }
        finally
        {
            foreach (var connectionString in connectionStrings)
                await DropAsync(connectionString, new SqlConnectionStringBuilder(connectionString).InitialCatalog);
        }
    }

    [Fact]
    public async Task Creating_an_existing_database_again_keeps_it()
    {
        var connectionString = TestDatabase.NewDatabase();
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        try
        {
            TestSqlServer.CreateDatabase(connectionString);     // the API host builder may run more than once

            Assert.True(await ExistsAsync(connectionString, name));
        }
        finally
        {
            await DropAsync(connectionString, name);
        }
    }

    [Theory]
    [InlineData("master")]
    [InlineData("LibraryApp")]
    [InlineData("LibraryApp_Tests_x]; DROP DATABASE [LibraryApp")]
    public void Only_a_test_database_name_is_created(string name)
    {
        var connectionString = new SqlConnectionStringBuilder(TestDatabase.NewConnectionString()) { InitialCatalog = name }
            .ConnectionString;

        Assert.Throws<ArgumentException>(() => TestSqlServer.CreateDatabase(connectionString));
    }

    private static async Task<SqlConnection> OpenMasterAsync(string connectionString)
    {
        var connection = new SqlConnection(
            new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@resource", TestSqlServer.CreateLockResource);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ExistsAsync(string connectionString, string name)
    {
        await using var connection = await OpenMasterAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.AddWithValue("@name", name);
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }

    private static async Task DropAsync(string connectionString, string name)
    {
        await using var connection = await OpenMasterAsync(connectionString);
        await using var command = connection.CreateCommand();
        // The name comes from TestDatabase (a prefix and 32 hex digits).
        command.CommandText = $"IF DB_ID(@name) IS NOT NULL DROP DATABASE [{name}]";
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync();
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LibraryApp.sln")))
                return directory.FullName;

        throw new InvalidOperationException("LibraryApp.sln not found above the test output folder.");
    }

    private static TempFile DotEnv(params string[] lines)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"testsqlserver-{Guid.NewGuid():N}.env");
        File.WriteAllLines(path, lines);
        return new TempFile(path);
    }

    private sealed record TempFile(string Path) : IDisposable
    {
        public void Dispose() => File.Delete(Path);
    }
}
