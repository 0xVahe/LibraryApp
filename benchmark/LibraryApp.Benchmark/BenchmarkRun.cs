using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;

namespace LibraryApp.Benchmark;

/// <summary>
/// One run: checks the API is ready, signs in once per role, then calls each endpoint sequentially — first the warm-up
/// calls (not counted: JIT, connection pool, query plans), then the measured ones. The time of a call is from sending the
/// request to the last byte of the response, so it includes Kestrel, authentication, the database and serialization.
/// </summary>
public static class BenchmarkRun
{
    public static async Task<int> ExecuteAsync(BenchmarkArguments arguments)
    {
        if (!arguments.AllowRemote && !Hosts.IsLocal(arguments.BaseUrl.Host))
            throw new BenchmarkException($"'{arguments.BaseUrl.Host}' is not this machine; pass --allow-remote to measure it anyway.");

        var startedAt = DateTimeOffset.UtcNow;
        var machine = MachineInfo.Read();
        var repository = RepositoryInfo.Read();
        var database = BenchmarkDatabase.TryOpen(arguments);
        var snapshot = database is null ? null : await database.SnapshotAsync();

        using var http = new HttpClient { BaseAddress = arguments.BaseUrl, Timeout = TimeSpan.FromSeconds(30) };
        await EnsureReadyAsync(http);

        var password = arguments.Password ?? Environment.GetEnvironmentVariable("LIBRARYAPP_BENCHMARK_PASSWORD") ?? StaffSeedData.Password;
        var tokens = new Dictionary<Caller, string>();
        foreach (var (caller, email) in Scenario.Accounts)
            tokens[caller] = await SignInAsync(http, email, password);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, Yerevan));
        var results = new List<EndpointResult>();
        foreach (var endpoint in Scenario.Endpoints(today))
        {
            var result = await MeasureAsync(http, endpoint, tokens[endpoint.Caller], arguments.Warmup, arguments.Iterations);
            results.Add(result);
            Console.WriteLine($"{(result.Passed ? "pass" : "FAIL"),-4}  {endpoint.Name,-36} p50 {result.P50,7:F1} ms  " +
                              $"p95 {result.P95,7:F1} ms  max {result.Max,7:F1} ms  first {result.FirstCall,7:F1} ms  limit {endpoint.LimitMs} ms" +
                              (result.Errors > 0 ? $"  errors {result.Errors} ({result.FirstError})" : ""));
        }

        var report = new RunReport(arguments, startedAt, DateTimeOffset.UtcNow, machine, repository, snapshot, results);
        var file = report.Write(arguments.OutputDirectory ?? Path.Combine(repository.Root, "docs", "performance"));
        Console.WriteLine($"Report: {file}");

        return results.All(r => r.Passed) ? 0 : 1;
    }

    public static readonly TimeZoneInfo Yerevan = TimeZoneInfo.FindSystemTimeZoneById("Asia/Yerevan");

    private static async Task EnsureReadyAsync(HttpClient http)
    {
        try
        {
            using var response = await http.GetAsync("health/ready");
            if (!response.IsSuccessStatusCode)
                throw new BenchmarkException($"{http.BaseAddress}health/ready answered {(int)response.StatusCode}: the API is not ready.");
        }
        catch (HttpRequestException ex)
        {
            throw new BenchmarkException($"the API does not answer at {http.BaseAddress}: {ex.Message}");
        }
    }

    private static async Task<string> SignInAsync(HttpClient http, string email, string password)
    {
        using var response = await http.PostAsJsonAsync("api/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode)
            throw new BenchmarkException($"sign-in of {email} answered {(int)response.StatusCode}; is this the seeded Development database?");

        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        return body?.AccessToken ?? throw new BenchmarkException($"sign-in of {email} returned no access token.");
    }

    private static async Task<EndpointResult> MeasureAsync(HttpClient http, Endpoint endpoint, string token, int warmup, int iterations)
    {
        // The first call is kept apart: it is the only one before JIT, query plans and the application's caches are warm.
        double? firstCall = null;
        for (var i = 0; i < warmup; i++)
        {
            var (milliseconds, _) = await CallAsync(http, endpoint, token);
            firstCall ??= milliseconds;
        }

        var samples = new double[iterations];
        var errors = 0;
        string? firstError = null;
        for (var i = 0; i < iterations; i++)
        {
            var (milliseconds, status) = await CallAsync(http, endpoint, token);
            samples[i] = milliseconds;
            if (status is < 200 or > 299)
            {
                errors++;
                firstError ??= $"HTTP {status}";
            }
        }

        return new EndpointResult(endpoint, samples, errors, firstError, firstCall);
    }

    private static async Task<(double Milliseconds, int Status)> CallAsync(HttpClient http, Endpoint endpoint, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var started = Stopwatch.GetTimestamp();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await response.Content.CopyToAsync(Stream.Null);
        var elapsed = Stopwatch.GetElapsedTime(started);

        return (elapsed.TotalMilliseconds, (int)response.StatusCode);
    }

    private sealed record LoginBody(string? AccessToken);
}

/// <summary>Timings of one endpoint, in milliseconds. Percentiles by the nearest-rank method.</summary>
/// <param name="FirstCall">The first warm-up call, or null without warm-up; not part of the result.</param>
public sealed record EndpointResult(Endpoint Endpoint, double[] Samples, int Errors, string? FirstError, double? FirstCall)
{
    public double Min => Samples.Min();
    public double Mean => Samples.Average();
    public double P50 => Percentile(50);
    public double P95 => Percentile(95);
    public double Max => Samples.Max();

    /// <summary>
    /// NFR-01 gives a limit, not a percentile: the 95th percentile has to be within it, so one call slowed by the machine
    /// does not decide the result; the maximum is reported next to it. A failed call fails the endpoint.
    /// </summary>
    public bool Passed => Errors == 0 && P95 <= Endpoint.LimitMs;

    public double Percentile(int percent)
    {
        var sorted = Samples.Order().ToArray();
        var rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }
}
