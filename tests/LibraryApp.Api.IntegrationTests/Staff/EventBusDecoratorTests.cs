using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class EventBusDecoratorTests
{
    [Theory]
    [InlineData("Outbox", typeof(OutboxEventBus))]
    [InlineData("Direct", typeof(InProcessEventBus))]
    public void Decoration_preserves_the_configured_delivery_mode(string delivery, Type busType)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Delivery"] = delivery,
            ["Messaging:DispatcherEnabled"] = "false"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(options =>
            options.UseSqlServer("Server=localhost;Database=Unused;Integrated Security=true;TrustServerCertificate=true"));
        services.AddEventBus(configuration);
        services.DecorateEventBus<FailAfterPublishEventBus>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var bus = Assert.IsType<FailAfterPublishEventBus>(scope.ServiceProvider.GetRequiredService<IEventBus>());

        Assert.Equal(busType, bus.Inner.GetType());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_bus_publishes_before_failure_and_is_disposed_with_its_scope(bool useFactory)
    {
        var services = new ServiceCollection();
        if (useFactory)
            services.AddScoped<IEventBus>(_ => new RecordingEventBus());
        else
            services.AddScoped<IEventBus, RecordingEventBus>();
        services.DecorateEventBus<FailAfterPublishEventBus>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        RecordingEventBus inner;
        using (var scope = provider.CreateScope())
        {
            var bus = Assert.IsType<FailAfterPublishEventBus>(scope.ServiceProvider.GetRequiredService<IEventBus>());
            inner = Assert.IsType<RecordingEventBus>(bus.Inner);
            Assert.Same(bus, scope.ServiceProvider.GetRequiredService<IEventBus>());
            var domainEvent = new TestEvent(Guid.NewGuid(), DateTime.UtcNow);
            using var cancellation = new CancellationTokenSource();

            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(domainEvent, cancellation.Token));

            Assert.Same(domainEvent, inner.PublishedEvent);
            Assert.Equal(cancellation.Token, inner.Token);
            Assert.False(inner.Disposed);
        }
        Assert.True(inner.Disposed);
    }

    [Fact]
    public void Instance_registration_is_preserved_across_scopes()
    {
        var inner = new RecordingEventBus();
        var services = new ServiceCollection();
        services.AddSingleton<IEventBus>(inner);
        services.DecorateEventBus<FailAfterPublishEventBus>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var bus = Assert.IsType<FailAfterPublishEventBus>(first.ServiceProvider.GetRequiredService<IEventBus>());

        Assert.Same(inner, bus.Inner);
        Assert.Same(bus, second.ServiceProvider.GetRequiredService<IEventBus>());
    }

    private sealed record TestEvent(Guid EventId, DateTime OccurredAtUtc) : IDomainEvent;

    private sealed class RecordingEventBus : IEventBus, IDisposable
    {
        public IDomainEvent? PublishedEvent { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Disposed { get; private set; }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class, IDomainEvent
        {
            PublishedEvent = domainEvent;
            Token = ct;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FailAfterPublishEventBus(IEventBus inner) : IEventBus
    {
        public IEventBus Inner => inner;

        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class, IDomainEvent
        {
            await inner.PublishAsync(domainEvent, ct);
            throw new InvalidOperationException("Simulated failure after publishing.");
        }
    }
}
