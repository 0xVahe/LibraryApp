using System.Globalization;
using LibraryApp.Benchmark;

// NFR-01: "Typical API calls respond in under 500 ms; reports in under 2 s on 10,000 loans."
//   seed   adds synthetic returned loans until the database holds the target number of loans
//   clean  removes the loans added by seed
//   run    times the API over HTTP and writes a report with its manifest to docs/performance/
// Exit codes: 0 every endpoint within its limit, 1 a limit exceeded or a request failed, 2 the run could not be done.

// Numbers in the console and the report look the same on every machine.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

try
{
    var arguments = BenchmarkArguments.Parse(args);
    DotNetEnv.Env.TraversePath().NoClobber().Load();

    return arguments.Command switch
    {
        "seed" => await BenchmarkDatabase.Open(arguments).SeedAsync(arguments.TotalLoans),
        "clean" => await BenchmarkDatabase.Open(arguments).CleanAsync(),
        "run" => await BenchmarkRun.ExecuteAsync(arguments),
        _ => Usage(),
    };
}
catch (BenchmarkException ex)
{
    Console.Error.WriteLine($"benchmark: {ex.Message}");
    return 2;
}

static int Usage()
{
    Console.WriteLine("""
        Usage: dotnet run --project benchmark/LibraryApp.Benchmark -c Release -- <command> [options]

          seed   [--total 10000] [--connection <string>] [--allow-remote]
          clean  [--connection <string>] [--allow-remote]
          run    [--base-url http://localhost:5055] [--iterations 50] [--warmup 5] [--out docs/performance]
                 [--password <seed password>] [--connection <string>] [--allow-remote]

        The connection string is ConnectionStrings__LibraryDb (environment or the repository .env) unless --connection is
        given. Only local servers are accepted without --allow-remote. See README, "Performance benchmark".
        """);
    return 2;
}
