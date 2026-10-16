using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Application.Reporting.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using LibraryApp.Domain.Borrowing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class ReportingRegistrationExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
	services.AddSingleton<PenaltyPolicy>();
        services.AddScoped<IReportingReadRepository, ReportingReadRepository>();
        services.AddScoped<IReportingService, ReportingService>();
        return services;
    }
}
