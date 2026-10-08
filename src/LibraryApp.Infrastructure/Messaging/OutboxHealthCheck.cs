using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Messaging;

/// <summary>
/// Degraded (HTTP 200 on /health/ready, visible in the body) when events could not be delivered after all attempts
/// (dead letters): nothing is lost, but someone must look at <c>OutboxMessages.LastError</c>.
/// </summary>
public sealed class OutboxHealthCheck(LibraryDbContext db, IOptions<MessagingOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var maxAttempts = options.Value.MaxAttempts;
        var deadLetters = await db.Set<OutboxMessage>()
            .CountAsync(m => m.ProcessedAtUtc == null && m.Attempts >= maxAttempts, cancellationToken);

        return deadLetters == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{deadLetters} event(s) could not be delivered; see OutboxMessages.LastError.");
    }
}
