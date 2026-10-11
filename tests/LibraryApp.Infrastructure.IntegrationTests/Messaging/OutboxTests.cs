using System.Collections.Concurrent;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Events;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LibraryApp.Infrastructure.IntegrationTests.Messaging;

/// <summary>The platform as the API registers it, on a throw-away SQL Server database with all migrations.</summary>
public sealed class OutboxFixture : IAsyncLifetime
{
    public const int MaxAttempts = 3;

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:LibraryDb"] = TestDatabase.NewConnectionString(),
            ["Messaging:DispatcherEnabled"] = "false",          // tests deliver by hand
            ["Messaging:MaxAttempts"] = MaxAttempts.ToString(),
            ["Messaging:RetryBaseDelay"] = "00:00:00",           // a failed message is due again at once
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatform(configuration, new TestEnvironment());

        // Only the handlers of this test project: module handlers come and go and must not change what these tests count.
        var moduleHandlers = services.Where(d => d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericTypeDefinition() == typeof(IEventHandler<>)).ToList();
        foreach (var handler in moduleHandlers)
            services.Remove(handler);
        services.AddEventHandlers(typeof(OutboxFixture).Assembly);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }

    public OutboxDispatcher Dispatcher => Services.GetRequiredService<OutboxDispatcher>();

    public async Task<LoanIssued> PublishAsync()
    {
        using var scope = Services.CreateScope();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var loanIssued = new LoanIssued(ids.NewId(), clock.UtcNow, ids.NewId(), ids.NewId(), ids.NewId(), ids.NewId(), clock.UtcNow.AddDays(21));
        await unitOfWork.ExecuteInTransactionAsync(ct => bus.PublishAsync(loanIssued, ct));
        return loanIssued;
    }

    public async Task<OutboxMessage?> MessageAsync(Guid eventId)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Set<OutboxMessage>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == eventId);
    }

    public async Task<int> InboxCountAsync(Guid eventId)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Set<ProcessedEvent>().CountAsync(p => p.EventId == eventId);
    }

    public async Task ExecuteSqlAsync(FormattableString sql)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.ExecuteSqlInterpolatedAsync(sql);
    }

    /// <summary>Delivers until nothing is due (several rounds, because a failed message is due again at once).</summary>
    public async Task DeliverAllAsync(int rounds = MaxAttempts + 1)
    {
        for (var i = 0; i < rounds; i++)
            if (await Dispatcher.DeliverDueAsync(CancellationToken.None) == 0)
                return;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LibraryApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>Counts calls per event id; can be told to fail for chosen event ids.</summary>
public static class HandlerLog
{
    public static readonly ConcurrentDictionary<(string Handler, Guid EventId), int> Calls = new();
    public static readonly ConcurrentDictionary<Guid, bool> FailFlaky = new();
    public static readonly ConcurrentDictionary<Guid, bool> AlwaysFail = new();

    public static int CallsOf(string handler, Guid eventId) => Calls.GetValueOrDefault((handler, eventId));

    public static void Record(string handler, Guid eventId) => Calls.AddOrUpdate((handler, eventId), 1, (_, n) => n + 1);
}

public sealed class SteadyHandler : IEventHandler<LoanIssued>
{
    public Task HandleAsync(LoanIssued domainEvent, CancellationToken ct)
    {
        HandlerLog.Record(nameof(SteadyHandler), domainEvent.EventId);
        return Task.CompletedTask;
    }
}

public sealed class FlakyHandler : IEventHandler<LoanIssued>
{
    public Task HandleAsync(LoanIssued domainEvent, CancellationToken ct)
    {
        HandlerLog.Record(nameof(FlakyHandler), domainEvent.EventId);

        if (HandlerLog.AlwaysFail.ContainsKey(domainEvent.EventId) || HandlerLog.FailFlaky.TryRemove(domainEvent.EventId, out _))
            throw new InvalidOperationException("flaky handler failed");

        return Task.CompletedTask;
    }
}

public class OutboxTests(OutboxFixture fixture) : IClassFixture<OutboxFixture>
{
    [Fact]
    public async Task Publishing_outside_a_transaction_is_refused()
    {
        using var scope = fixture.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bus.PublishAsync(new LoanIssued(Guid.NewGuid(), clock.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clock.UtcNow)));
    }

    [Fact]
    public async Task The_event_is_stored_with_the_change_and_disappears_with_its_rollback()
    {
        var stored = await fixture.PublishAsync();
        Assert.NotNull(await fixture.MessageAsync(stored.EventId));

        var eventId = Guid.NewGuid();
        using (var scope = fixture.Services.CreateScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await bus.PublishAsync(new LoanIssued(eventId, clock.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clock.UtcNow), ct);
                throw new InvalidOperationException("business rule failed");
            }));
        }

        Assert.Null(await fixture.MessageAsync(eventId));
    }

    [Fact]
    public async Task Every_handler_runs_once_and_the_message_is_processed()
    {
        var loanIssued = await fixture.PublishAsync();

        await fixture.DeliverAllAsync();

        Assert.Equal(1, HandlerLog.CallsOf(nameof(SteadyHandler), loanIssued.EventId));
        Assert.Equal(1, HandlerLog.CallsOf(nameof(FlakyHandler), loanIssued.EventId));
        Assert.Equal(2, await fixture.InboxCountAsync(loanIssued.EventId));
        Assert.NotNull((await fixture.MessageAsync(loanIssued.EventId))!.ProcessedAtUtc);
    }

    [Fact]
    public async Task A_redelivered_event_does_not_run_the_handlers_again()
    {
        var loanIssued = await fixture.PublishAsync();
        await fixture.DeliverAllAsync();

        // Simulates a crash after the handlers committed but before the message was marked processed.
        await fixture.ExecuteSqlAsync($"UPDATE [OutboxMessages] SET [ProcessedAtUtc] = NULL WHERE [Id] = {loanIssued.EventId}");
        await fixture.DeliverAllAsync();

        Assert.Equal(1, HandlerLog.CallsOf(nameof(SteadyHandler), loanIssued.EventId));
        Assert.Equal(1, HandlerLog.CallsOf(nameof(FlakyHandler), loanIssued.EventId));
        Assert.NotNull((await fixture.MessageAsync(loanIssued.EventId))!.ProcessedAtUtc);
    }

    [Fact]
    public async Task A_failing_handler_does_not_block_the_others_and_only_it_is_retried()
    {
        var loanIssued = await fixture.PublishAsync();
        HandlerLog.FailFlaky[loanIssued.EventId] = true;      // fails once, then works

        await fixture.Dispatcher.DeliverDueAsync(CancellationToken.None);

        var afterFailure = await fixture.MessageAsync(loanIssued.EventId);
        Assert.Null(afterFailure!.ProcessedAtUtc);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.Contains("flaky handler failed", afterFailure.LastError);
        Assert.Equal(1, HandlerLog.CallsOf(nameof(SteadyHandler), loanIssued.EventId));   // committed despite the other failure

        await fixture.DeliverAllAsync();

        Assert.Equal(1, HandlerLog.CallsOf(nameof(SteadyHandler), loanIssued.EventId));   // not repeated
        Assert.Equal(2, HandlerLog.CallsOf(nameof(FlakyHandler), loanIssued.EventId));    // failed once, then succeeded
        Assert.NotNull((await fixture.MessageAsync(loanIssued.EventId))!.ProcessedAtUtc);
    }

    [Fact]
    public async Task After_the_last_attempt_the_message_is_a_dead_letter_and_health_is_degraded()
    {
        var loanIssued = await fixture.PublishAsync();
        HandlerLog.AlwaysFail[loanIssued.EventId] = true;

        await fixture.DeliverAllAsync(OutboxFixture.MaxAttempts + 2);

        var message = await fixture.MessageAsync(loanIssued.EventId);
        Assert.Null(message!.ProcessedAtUtc);
        Assert.Equal(OutboxFixture.MaxAttempts, message.Attempts);
        Assert.Equal(OutboxFixture.MaxAttempts, HandlerLog.CallsOf(nameof(FlakyHandler), loanIssued.EventId));

        var health = await fixture.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(c => c.Name == "outbox");
        Assert.Equal(HealthStatus.Degraded, health.Status);

        await fixture.ExecuteSqlAsync($"DELETE FROM [OutboxMessages] WHERE [Id] = {loanIssued.EventId}");   // keep other tests healthy
    }

    [Fact]
    public async Task Two_dispatchers_at_the_same_time_deliver_each_event_once()
    {
        var events = new List<LoanIssued>();
        for (var i = 0; i < 10; i++)
            events.Add(await fixture.PublishAsync());

        var second = ActivatorUtilities.CreateInstance<OutboxDispatcher>(fixture.Services);
        await Task.WhenAll(fixture.DeliverAllAsync(), second.DeliverDueAsync(CancellationToken.None));
        await fixture.DeliverAllAsync();

        Assert.All(events, e => Assert.Equal(1, HandlerLog.CallsOf(nameof(SteadyHandler), e.EventId)));
    }

    [Fact]
    public async Task Cleanup_removes_old_processed_messages_and_inbox_records_but_keeps_pending_ones()
    {
        var old = await fixture.PublishAsync();
        await fixture.DeliverAllAsync();
        await fixture.ExecuteSqlAsync($"UPDATE [OutboxMessages] SET [ProcessedAtUtc] = DATEADD(day, -30, SYSUTCDATETIME()) WHERE [Id] = {old.EventId}");
        await fixture.ExecuteSqlAsync($"UPDATE [ProcessedEvents] SET [ProcessedAtUtc] = DATEADD(day, -30, SYSUTCDATETIME()) WHERE [EventId] = {old.EventId}");
        var pending = await fixture.PublishAsync();

        await fixture.Dispatcher.CleanupAsync(CancellationToken.None);

        Assert.Null(await fixture.MessageAsync(old.EventId));
        Assert.Equal(0, await fixture.InboxCountAsync(old.EventId));
        Assert.NotNull(await fixture.MessageAsync(pending.EventId));
        await fixture.DeliverAllAsync();
    }
}
