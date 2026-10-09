using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class CatalogRegistrationExtensions
{
    /// <summary>
    /// Catalog module services. Called from Program.cs. Each member adds the registrations of their own part here
    /// (repositories, services, seeder), one line per class, in their own pull request. Validators are registered automatically.
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Search (CAT-05)
        services.AddScoped<IBookSearchQuery, BookSearchQuery>();
        services.AddScoped<IBookSearchService, BookSearchService>();

        // Book details (CAT-06)
        services.AddScoped<IBookDetailsQuery, BookDetailsQuery>();
        services.AddScoped<IBookDetailsService, BookDetailsService>();

        // Copies (CAT-04)
        services.AddScoped<IBookCopyRepository, BookCopyRepository>();
        services.AddScoped<IBookCopyService, BookCopyService>();

        // Authors and book-author links (CAT-02)
        services.AddScoped<IAuthorRepository, AuthorRepository>();
        services.AddScoped<IAuthorService, AuthorService>();

        // Categories (CAT-03)
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryService, CategoryService>();

        // Catalog seed: categories, authors, publishers and books (D3)
        services.AddScoped<IModuleSeeder, CatalogSeeder>();

        return services;
    }
}
