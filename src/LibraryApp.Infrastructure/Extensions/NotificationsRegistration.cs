using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Infrastructure.Mail;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class NotificationsRegistrationExtensions
{
    /// <summary>Notifications module services. Called from Program.cs; each member adds their own registrations here.</summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Outlook sender: MailKit to smtp4dev (NOT-03, INT-08). A wrong "Mail" section stops the app at start.
        services.AddOptions<MailOptions>()
            .Bind(configuration.GetSection(MailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IOutlookNotificationSender, SmtpOutlookNotificationSender>();

        // In-app notifications of the current staff member (NOT-04)
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationService, NotificationService>();

        // Template renderer (NOT-02): stateless, so one instance.
        services.AddSingleton<INotificationTemplateRenderer, NotificationTemplateRenderer>();

        return services;
    }
}
