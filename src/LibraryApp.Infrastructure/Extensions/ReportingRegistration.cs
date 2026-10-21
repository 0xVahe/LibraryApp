using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Extensions;

public static class ReportingRegistrationExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
	services.AddOptions<ReportingOptions>()
            .Bind(configuration.GetSection(ReportingOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ReportingOptions>, ReportingOptionsValidator>();
        services.AddMemoryCache();
        services.AddScoped<IReportingReadRepository, ReportingReadRepository>();
        services.AddScoped<IReportingService, ReportingService>();
        return services;
    }
}
