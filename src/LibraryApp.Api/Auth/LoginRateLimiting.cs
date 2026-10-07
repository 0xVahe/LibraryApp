using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using LibraryApp.Api.ErrorHandling;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>
    /// Limits POST /api/auth/login per client IP (brute force across many accounts). Over the limit → 429.
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
                var problem = GlobalExceptionHandler.Problem(StatusCodes.Status429TooManyRequests, "Too many requests",
                    "Too many login attempts. Try again later.", "too_many_requests");
                problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", ct);
            };
        });

        return services;
    }
}