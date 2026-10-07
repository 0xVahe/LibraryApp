using System.Data;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Messaging;

/// <summary>
/// Delivers stored events (SD-07). One round:
/// <list type="number">
/// <item>Claims up to <see cref="MessagingOptions.BatchSize"/> due messages with a lease (<c>UPDLOCK, READPAST</c>):
/// several API instances never take the same message; after a crash the lease expires and the message is due again.</item>
/// <item>Runs every handler of the event in its OWN scope and transaction, together with an inbox record
/// (<see cref="ProcessedEvent"/>): a handler that already processed the event is skipped, a failing handler does not
/// block the others, and a retry re-runs only the handlers that have not succeeded.</item>
/// <item>All handlers done → the message is processed. Otherwise it is retried after an exponential delay; after
/// <see cref="MessagingOptions.MaxAttempts"/> it stays as a dead letter (logged, reported by the health check).</item>
/// </list>
/// Rules for handlers: change data only through the scoped <c>LibraryDbContext</c> (no SaveChanges, no own
/// transaction, no e-mail or HTTP inside); external work is done later from stored rows (for example a Pending notification).
/// Processed messages and inbox records are deleted after <see cref="MessagingOptions.Retention"/>.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    EventTypeRegistry registry,
    EventHandlerInvoker invoker,
    IClock clock,
    IOptions<MessagingOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int CleanupBatchSize = 1000;

    private const string ClaimSql = """
        WITH due AS (
            SELECT TOP (@batch) [Id], [Type], [Payload], [Attempts], [LockedUntilUtc]
            FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [ProcessedAtUtc] IS NULL AND [Attempts] < @maxAttempts
              AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= @now)
              AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] <= @now)
            ORDER BY [OccurredAtUtc], [Id])
        UPDATE due SET [LockedUntilUtc] = @leaseUntil
        OUTPUT inserted.[Id], inserted.[Type], inserted.[Payload], inserted.[Attempts]
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var nextCleanup = clock.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = 0;

            try
            {
                delivered = await DeliverDueAsync(stoppingToken);

                if (clock.UtcNow >= nextCleanup)
                {
                    await CleanupAsync(stoppingToken);
                    nextCleanup = clock.UtcNow + settings.CleanupInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatcher round failed");
            }

            if (delivered > 0)
                continue;

            try { await Task.Delay(settings.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Claims and delivers one batch of due messages. Returns how many were claimed.</summary>
    public async Task<int> DeliverDueAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var now = clock.UtcNow;

        List<ClaimedOutboxMessage> claimed;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            claimed = await db.Database.SqlQueryRaw<ClaimedOutboxMessage>(
                    ClaimSql,
                    new SqlParameter("@batch", SqlDbType.Int) { Value = settings.BatchSize },
                    new SqlParameter("@maxAttempts", SqlDbType.Int) { Value = settings.MaxAttempts },
                    new SqlParameter("@now", SqlDbType.DateTime2) { Value = now },
                    new SqlParameter("@leaseUntil", SqlDbType.DateTime2) { Value = now + settings.LeaseDuration })
                .ToListAsync(ct);
        }

        foreach (var message in claimed)
            await DeliverAsync(message, ct);

        return claimed.Count;
    }

    /// <summary>Deletes processed messages and inbox records older than the retention period. Dead letters stay.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow - options.Value.Retention;
        var deleted = 0;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        deleted += await DeleteInBatchesAsync(db,
            "DELETE TOP (@batch) FROM [OutboxMessages] WHERE [ProcessedAtUtc] IS NOT NULL AND [ProcessedAtUtc] < @cutoff", cutoff, ct);
        deleted += await DeleteInBatchesAsync(db,
            "DELETE TOP (@batch) FROM [ProcessedEvents] WHERE [ProcessedAtUtc] < @cutoff", cutoff, ct);

        if (deleted > 0)
            logger.LogInformation("Outbox cleanup removed {Count} rows older than {Cutoff:u}", deleted, cutoff);

        return deleted;
    }

    private async Task DeliverAsync(ClaimedOutboxMessage message, CancellationToken ct)
    {
        var failures = new List<string>();

        try
        {
            var eventType = registry.Resolve(message.Type);
            var domainEvent = (IDomainEvent)(JsonSerializer.Deserialize(message.Payload, eventType)
                ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload."));

            IReadOnlyList<string> handlers;
            await using (var scope = scopes.CreateAsyncScope())
                handlers = invoker.HandlerNames(eventType, scope.ServiceProvider);

            foreach (var handler in handlers)
            {
                try
                {
                    await RunHandlerOnceAsync(domainEvent, handler, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Handler {Handler} failed for {EventType} {EventId}", handler, message.Type, message.Id);
                    failures.Add($"{handler}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Outbox message {MessageId} ({EventType}) cannot be read", message.Id, message.Type);
            failures.Add($"{ex.GetType().Name}: {ex.Message}");
        }

        await using var resultScope = scopes.CreateAsyncScope();
        var db = resultScope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var now = clock.UtcNow;

        if (failures.Count == 0)
        {
            await db.Set<OutboxMessage>().Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAtUtc, now)
                .SetProperty(m => m.LockedUntilUtc, (DateTime?)null)
                .SetProperty(m => m.LastError, (string?)null), ct);
            return;
        }

        var attempts = message.Attempts + 1;
        var next = now + options.Value.RetryDelay(attempts);
        var error = string.Join(Environment.NewLine, failures);
        if (error.Length > OutboxMessage.LastErrorMaxLength)
            error = error[..OutboxMessage.LastErrorMaxLength];

        await db.Set<OutboxMessage>().Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Attempts, attempts)
            .SetProperty(m => m.NextAttemptAtUtc, next)
            .SetProperty(m => m.LastError, error)
            .SetProperty(m => m.LockedUntilUtc, (DateTime?)null), ct);

        if (attempts >= options.Value.MaxAttempts)
            logger.LogError("Outbox message {MessageId} ({EventType}) failed {Attempts} times and is no longer retried (dead letter): {Error}",
                message.Id, message.Type, attempts, error);
    }

    /// <summary>One handler, one event, one transaction: skipped if the inbox already has it; otherwise run and recorded.</summary>
    private async Task RunHandlerOnceAsync(IDomainEvent domainEvent, string handler, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<LibraryDbContext>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await db.Set<ProcessedEvent>().AnyAsync(p => p.EventId == domainEvent.EventId && p.Handler == handler, token))
                return;

            await invoker.InvokeAsync(domainEvent, handler, services, token);
            db.Set<ProcessedEvent>().Add(ProcessedEvent.Create(domainEvent.EventId, handler, clock.UtcNow));
        }, ct);
    }

    private static async Task<int> DeleteInBatchesAsync(LibraryDbContext db, string sql, DateTime cutoff, CancellationToken ct)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlRawAsync(sql,
                [
                    new SqlParameter("@batch", SqlDbType.Int) { Value = CleanupBatchSize },
                    new SqlParameter("@cutoff", SqlDbType.DateTime2) { Value = cutoff },
                ], ct);
            total += deleted;
        }
        while (deleted == CleanupBatchSize);

        return total;
    }

    private sealed class ClaimedOutboxMessage
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = "";
        public string Payload { get; set; } = "";
        public int Attempts { get; set; }
    }
}
