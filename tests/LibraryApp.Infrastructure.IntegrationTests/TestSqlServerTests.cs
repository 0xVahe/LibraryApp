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
            "Use TestDatabase.NewConnectionString() or ApiHostFactory instead of choosing a server in: " + string.Join(", ", offenders));
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
