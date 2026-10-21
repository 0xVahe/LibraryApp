using System.Collections.Concurrent;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.IntegrationTests.Identity;
using LibraryApp.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Messaging;

public sealed class StaffEnrolledEventHandlerTests
{
    [Fact]
    public async Task Direct_event_bus_discovers_and_invokes_staff_enrolled_handler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Delivery"] = nameof(EventDeliveryMode.Direct),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventBus(configuration);
        services.AddScoped<INotificationCreator, NoOpNotificationCreator>();
        services.AddScoped<ICatalogService, StubCatalogService>();
        services.AddSingleton<IClock>(new FixedClock(new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc)));   // these tests are about discovery, not about the notification

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var eventId = Guid.NewGuid();
        var domainEvent = new StaffEnrolled(
            eventId,
            new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(domainEvent, cancellation.Token));
    }

    [Fact]
    public async Task Direct_event_bus_discovers_and_invokes_all_notification_handlers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Delivery"] = nameof(EventDeliveryMode.Direct),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventBus(configuration);
        services.AddScoped<INotificationCreator, NoOpNotificationCreator>();
        services.AddScoped<ICatalogService, StubCatalogService>();
        services.AddSingleton<IClock>(new FixedClock(new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc)));   // these tests are about discovery, not about the notification

        var invocations = new HandlerInvocationRecorder();
        RegisterRecordingHandler<StaffEnrolled, StaffEnrolledEventHandler>(services, invocations);
        RegisterRecordingHandler<LoanIssued, LoanIssuedEventHandler>(services, invocations);
        RegisterRecordingHandler<LoanOverdue, LoanOverdueEventHandler>(services, invocations);
        RegisterRecordingHandler<LoanRenewed, LoanRenewedEventHandler>(services, invocations);
        RegisterRecordingHandler<LoanReturned, LoanReturnedEventHandler>(services, invocations);
        RegisterRecordingHandler<ReservationReady, ReservationReadyEventHandler>(services, invocations);
        RegisterRecordingHandler<PenaltyApplied, PenaltyAppliedEventHandler>(services, invocations);
        RegisterRecordingHandler<StaffStatusChanged, StaffStatusChangedEventHandler>(services, invocations);
        RegisterRecordingHandler<StaffPasswordChanged, StaffPasswordChangedEventHandler>(services, invocations);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var occurredAtUtc = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var events = new IDomainEvent[]
        {
            new StaffEnrolled(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid()),
            new LoanIssued(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAtUtc.AddDays(21)),
            new LoanOverdue(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAtUtc.AddDays(-1)),
            new LoanRenewed(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAtUtc.AddDays(21), 1),
            new LoanReturned(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAtUtc, 0),
            new ReservationReady(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), occurredAtUtc.AddDays(3)),
            new PenaltyApplied(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), Guid.NewGuid(), 1, 1, "Late return"),
            new StaffStatusChanged(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid(), StaffMemberStatus.Active, StaffMemberStatus.Restricted, "Penalty threshold", null),
            new StaffPasswordChanged(Guid.NewGuid(), occurredAtUtc, Guid.NewGuid()),
        };

        foreach (var domainEvent in events)
            await PublishAsync(bus, domainEvent);

        Assert.Equal(events.Length, invocations.Count);
        Assert.All(events, domainEvent => Assert.True(invocations.WasInvoked(domainEvent.EventId)));
    }

    private static void RegisterRecordingHandler<TEvent, THandler>(IServiceCollection services, HandlerInvocationRecorder invocations)
        where TEvent : class, IDomainEvent
        where THandler : class, IEventHandler<TEvent>
    {
        var contract = typeof(IEventHandler<TEvent>);
        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == contract && descriptor.ImplementationType == typeof(THandler));

        services.Remove(registration);
        services.AddScoped<THandler>();
        services.AddScoped<IEventHandler<TEvent>>(provider =>
            new RecordingEventHandler<TEvent>(provider.GetRequiredService<THandler>(), invocations));
    }

    private static Task PublishAsync<TEvent>(IEventBus bus, IDomainEvent domainEvent)
        where TEvent : class, IDomainEvent =>
        bus.PublishAsync((TEvent)domainEvent);

    private static Task PublishAsync(IEventBus bus, IDomainEvent domainEvent) => domainEvent switch
    {
        StaffEnrolled value => PublishAsync<StaffEnrolled>(bus, value),
        LoanIssued value => PublishAsync<LoanIssued>(bus, value),
        LoanOverdue value => PublishAsync<LoanOverdue>(bus, value),
        LoanRenewed value => PublishAsync<LoanRenewed>(bus, value),
        LoanReturned value => PublishAsync<LoanReturned>(bus, value),
        ReservationReady value => PublishAsync<ReservationReady>(bus, value),
        PenaltyApplied value => PublishAsync<PenaltyApplied>(bus, value),
        StaffStatusChanged value => PublishAsync<StaffStatusChanged>(bus, value),
        StaffPasswordChanged value => PublishAsync<StaffPasswordChanged>(bus, value),
        _ => throw new ArgumentOutOfRangeException(nameof(domainEvent), domainEvent.GetType().Name),
    };

    private sealed class RecordingEventHandler<TEvent>(IEventHandler<TEvent> inner, HandlerInvocationRecorder invocations)
        : IEventHandler<TEvent>
        where TEvent : class, IDomainEvent
    {
        public async Task HandleAsync(TEvent domainEvent, CancellationToken ct)
        {
            await inner.HandleAsync(domainEvent, ct);
            invocations.Record(domainEvent.EventId);
        }
    }

    private sealed class NoOpNotificationCreator : INotificationCreator
    {
        public Task CreateAsync(NotificationType type, Guid staffMemberId, Guid eventId,
            IReadOnlyCollection<NotificationChannel> channels, IReadOnlyDictionary<string, string> values, Guid? loanId = null, bool ccLineManager = false, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubCatalogService : ICatalogService
    {
        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult<BookDto?>(new BookDto(bookId, "Clean Code", "9780132350884", 2008, "English", null,
                Guid.NewGuid(), "Software", null, null, ["Robert C. Martin"], 1, 1, false));

        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class HandlerInvocationRecorder
    {
        private readonly ConcurrentDictionary<Guid, byte> _eventIds = new();

        public int Count => _eventIds.Count;

        public void Record(Guid eventId) => _eventIds.TryAdd(eventId, 0);

        public bool WasInvoked(Guid eventId) => _eventIds.ContainsKey(eventId);
    }
}
