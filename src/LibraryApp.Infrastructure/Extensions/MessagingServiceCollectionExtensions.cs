using System.Reflection;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Event bus (INT-06, SD-06/07): outbox by default, direct delivery as a fallback (<c>Messaging:Delivery</c>).
    /// Every <see cref="IEventHandler{TEvent}"/> in the Application assembly is registered automatically.
    /// </summary>
    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MessagingOptions.SectionName);
        services.Configure<MessagingOptions>(section);
        var settings = section.Get<MessagingOptions>() ?? new MessagingOptions();

        services.AddSingleton(new EventTypeRegistry([typeof(IDomainEvent).Assembly]));
        services.AddSingleton<EventHandlerInvoker>();
        services.AddEventHandlers(typeof(IEventHandler<>).Assembly);

        if (settings.Delivery == EventDeliveryMode.Direct)
        {
            services.AddScoped<IEventBus, InProcessEventBus>();
            return services;
        }

        services.AddScoped<IEventBus, OutboxEventBus>();
        services.AddSingleton<OutboxDispatcher>();
        if (settings.DispatcherEnabled)
            services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

        return services;
    }

    /// <summary>Registers every closed implementation of <see cref="IEventHandler{TEvent}"/> found in the assembly (scoped).</summary>
    public static IServiceCollection AddEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)))
            {
                if (services.Any(d => d.ServiceType == contract && d.ImplementationType == type))
                    continue;

                services.AddScoped(contract, type);
            }
        }

        return services;
    }
}
