using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class CatalogRegistrationExtensions
{
    /// <summary>
    /// Catalog module services. Called from Program.cs. Empty on purpose: each member adds the registrations of their own
    /// part here (repositories, services, validators, seeder), one line per class, in their own pull request.
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
