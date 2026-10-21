using System.Globalization;

namespace LibraryApp.Benchmark;

/// <summary>A failure that stops the benchmark with exit code 2 and a message, without a stack trace.</summary>
public sealed class BenchmarkException(string message) : Exception(message);

/// <summary>Command-line options. Every value has a default, so a bare command measures what NFR-01 names.</summary>
public sealed record BenchmarkArguments(
    string Command,
    int TotalLoans,
    string? ConnectionString,
    bool AllowRemote,
    Uri BaseUrl,
    int Iterations,
    int Warmup,
    string? OutputDirectory,
    string? Password)
{
    /// <summary>NFR-01 measures the API "on 10,000 loans".</summary>
    public const int DefaultTotalLoans = 10_000;

    public static BenchmarkArguments Parse(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal))
                throw new BenchmarkException($"unexpected argument '{name}'.");

            if (name == "--allow-remote")
            {
                options[name] = "true";
                continue;
            }

            if (i + 1 >= args.Length)
                throw new BenchmarkException($"{name} needs a value.");
            options[name] = args[++i];
        }

        string? Text(string name) => options.Remove(name, out var value) ? value : null;

        int Number(string name, int fallback, int min, int max)
        {
            var text = Text(name);
            if (text is null)
                return fallback;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw new BenchmarkException($"{name} must be a whole number from {min} to {max}.");
            return value;
        }

        var baseUrlText = Text("--base-url") ?? "http://localhost:5055";
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https"))
            throw new BenchmarkException("--base-url must be an http or https address.");

        var parsed = new BenchmarkArguments(
            command,
            Number("--total", DefaultTotalLoans, 1, 1_000_000),
            Text("--connection"),
            Text("--allow-remote") is not null,
            baseUrl,
            Number("--iterations", 50, 10, 10_000),
            Number("--warmup", 5, 0, 1_000),
            Text("--out"),
            Text("--password"));

        if (options.Count > 0)
            throw new BenchmarkException($"unknown option '{options.Keys.First()}'.");

        return parsed;
    }
}
