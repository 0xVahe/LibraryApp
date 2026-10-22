using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Infrastructure.Catalog;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Extensions;

public static class CatalogRegistrationExtensions
{
    /// <summary>
    /// The Catalog module, called from Program.cs: the cover and import settings, repositories, queries, services and the
    /// seeder.
    /// Validators are registered automatically (AddValidatorsFromAssembly in AddPlatform).
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogCoversOptions>()
            .Bind(configuration.GetSection(CatalogCoversOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CatalogCoversOptions>, CatalogCoversOptionsValidator>();
        services.AddSingleton<ICatalogCoverUrlBuilder, CatalogCoverUrlBuilder>();

        // Limits of the book CSV import (CAT-09): required, checked at start.
        services.AddOptions<CatalogImportOptions>()
            .Bind(configuration.GetSection(CatalogImportOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CatalogImportOptions>, CatalogImportOptionsValidator>();

        // Search (CAT-05)
        services.AddScoped<IBookSearchQuery, BookSearchQuery>();
        services.AddScoped<IBookSearchService, BookSearchService>();

        // Book details (CAT-06)
        services.AddScoped<IBookDetailsQuery, BookDetailsQuery>();
        services.AddScoped<IBookDetailsService, BookDetailsService>();

        // Copies (CAT-04)
        services.AddScoped<IBookCopyRepository, BookCopyRepository>();
        services.AddScoped<IBookCopyService, BookCopyService>();

        // Contract for other modules: available copy, copy status, book lookup (CAT-07, INT-01, INT-02).
        // Scoped: SetCopyStatusAsync changes the copy in the caller's DbContext, so the caller's unit of work saves it.
        services.AddScoped<ICatalogCopyQuery, CatalogCopyQuery>();
        services.AddScoped<ICatalogService, CatalogService>();

        // Authors and book-author links (CAT-02)
        services.AddScoped<IAuthorRepository, AuthorRepository>();
        services.AddScoped<IAuthorService, AuthorService>();

        // Publishers
        services.AddScoped<IPublisherRepository, PublisherRepository>();
        services.AddScoped<IPublisherService, PublisherService>();

        // Categories (CAT-03)
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryService, CategoryService>();

        // Book CRUD (CAT-01)
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IBookService, BookService>();

        // Book CSV import (CAT-09)
        services.AddSingleton<IBookCsvParser, BookCsvParser>();
        services.AddScoped<IBookImportService, BookImportService>();

        // Book purchase suggestions (CAT-10)
        services.AddScoped<IBookSuggestionRepository, BookSuggestionRepository>();
        services.AddScoped<IBookSuggestionService, BookSuggestionService>();

        // Catalog seed: categories, authors, publishers, books and copies (D3)
        services.AddScoped<IModuleSeeder, CatalogSeeder>();

        return services;
    }
}
