using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using LibraryApp.Api.ErrorHandling;
using LibraryApp.Api.Ui;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Auth;

/// <summary>Settings section "LoginRateLimit": login attempts per client IP per window.</summary>
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "LoginRateLimit";

    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}

public static class LoginRateLimiting
{
    public const string PolicyName = "login";

    /// <summary>Query flag of a page whose POST was rejected; the page shows "Too many attempts".</summary>
    public const string TooManyAttemptsParameter = "tooManyAttempts";

    /// <summary>
    /// Limits POST /api/auth/login, /api/auth/change-password and the pages with this policy per client IP (brute force
    /// across many accounts). Over the limit: 429 ProblemDetails for the API; for a page, 303 back to the same page with
    /// <see cref="TooManyAttemptsParameter"/>. Both send <c>Retry-After</c>.
    /// Behind a reverse proxy, configure ForwardedHeaders with the known proxies, otherwise every user shares the
    /// proxy's address and therefore one limit.
    /// </summary>
    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LoginRateLimitOptions>().Bind(configuration.GetSection(LoginRateLimitOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(PolicyName, httpContext =>
            {
                // Only POSTs count: the Login page itself (GET) stays open, also after a rejected POST sends the user back to it.
                if (!HttpMethods.IsPost(httpContext.Request.Method))
                    return RateLimitPartition.GetNoLimiter("get");

                var settings = httpContext.RequestServices.GetRequiredService<IOptions<LoginRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                // A page cannot show ProblemDetails: 303 back to the same page (GET), which says "Too many attempts".
                if (!UiPaths.IsApi(http))
                {
                    http.Response.StatusCode = StatusCodes.Status303SeeOther;
                    http.Response.Headers.Location = TooManyAttemptsUrl(http.Request);
                    return;
                }

                var detail = http.Request.Path.StartsWithSegments("/api/auth/login")
                    ? "Too many login attempts. Try again later."
                    : null;   // HttpErrors default: "Too many requests. Try again later."
                var problem = GlobalExceptionHandler.Problem(HttpErrors.TooManyRequests, detail);
                problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;

                await http.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", ct);
            };
        });

        return services;
    }

    /// <summary>The rejected page itself with its query (e.g. <c>returnUrl</c>) plus <see cref="TooManyAttemptsParameter"/>.</summary>
    public static string TooManyAttemptsUrl(HttpRequest request)
    {
        var query = QueryHelpers.ParseQuery(request.QueryString.Value)
            .Where(pair => !string.Equals(pair.Key, TooManyAttemptsParameter, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))
            .Append(new(TooManyAttemptsParameter, "true"));
        return QueryHelpers.AddQueryString((request.PathBase + request.Path).ToString(), query);
    }
}
