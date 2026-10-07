using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

namespace LibraryApp.Infrastructure.Extensions;

public static class CatalogRegistrationExtensions
{
    /// <summary>
    /// Catalog module services. Called from Program.cs. Empty on purpose: each member adds the registrations of their own
    /// part here (repositories, services, validators, seeder), one line per class, in their own pull request.
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBookSearchQuery, BookSearchQuery>();
        services.AddScoped<IBookSearchService, BookSearchService>();
        return services;
    }
}
