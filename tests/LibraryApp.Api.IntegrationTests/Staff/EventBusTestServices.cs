using LibraryApp.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

internal static class EventBusTestServices
{
    /// <summary>Wraps the configured bus without changing its delivery mode, lifetime or dependencies.</summary>
    public static void DecorateEventBus<TDecorator>(this IServiceCollection services) where TDecorator : class, IEventBus
    {
        var original = services.Last(d => d.ServiceType == typeof(IEventBus) && !d.IsKeyedService);
        var key = new object();
        var inner = original.ImplementationType is { } type
            ? ServiceDescriptor.DescribeKeyed(typeof(IEventBus), key, type, original.Lifetime)
            : original.ImplementationFactory is { } factory
                ? ServiceDescriptor.DescribeKeyed(typeof(IEventBus), key, (provider, _) => factory(provider), original.Lifetime)
                : ServiceDescriptor.KeyedSingleton(typeof(IEventBus), key, original.ImplementationInstance!);

        services.Remove(original);
        services.Add(inner);
        services.Add(ServiceDescriptor.Describe(typeof(IEventBus), provider =>
            ActivatorUtilities.CreateInstance<TDecorator>(provider, provider.GetRequiredKeyedService<IEventBus>(key)),
            original.Lifetime));
    }
}
