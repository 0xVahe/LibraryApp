using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Infrastructure.Mail;
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

        return services;
    }
}
