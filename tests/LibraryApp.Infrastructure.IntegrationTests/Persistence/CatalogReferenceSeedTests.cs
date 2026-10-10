using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public class CatalogReferenceSeedTests
{
    [Fact]
    public Task Initial_initialization_creates_all_required_references() => WithDatabaseAsync(async db =>
    {
        await InitializeAsync(db);

        await AssertReferencesAsync(db);
        Assert.Equal(CatalogSeedData.Books.Count, await db.Books.CountAsync());
        Assert.Empty(await db.BookCopies.ToListAsync());
    });

    [Fact]
    public Task Repeated_initialization_preserves_ids_without_duplicates() => WithDatabaseAsync(async db =>
    {
        await InitializeAsync(db);
        await AssertReferencesAsync(db);

        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(db.Database.GetConnectionString()!);
        await using var restarted = new LibraryDbContext(options.Options);
        await InitializeAsync(restarted);

        await AssertReferencesAsync(restarted);
    });

    [Fact]
    public Task Partial_initialization_inserts_only_missing_references() => WithDatabaseAsync(async db =>
    {
        await new UnitOfWork(db).ExecuteInTransactionAsync(_ =>
        {
            foreach (var (id, name) in CatalogSeedData.Categories.Take(3))
                db.Categories.Add(new Category(id, name));
            foreach (var (id, name) in CatalogSeedData.Authors.Take(30))
                db.Authors.Add(new Author(id, name));
            foreach (var (id, name) in CatalogSeedData.Publishers.Take(10))
                db.Publishers.Add(new Publisher(id, name));
            return Task.CompletedTask;
        });

        db.ChangeTracker.Clear();
        await InitializeAsync(db);

        await AssertReferencesAsync(db);
    });

    [Fact]
    public Task Existing_reference_edits_and_other_rows_are_preserved() => WithDatabaseAsync(async db =>
    {
        var authorId = new Guid("20000000-0000-0000-0002-000000000001");
        var publisherId = new Guid("20000000-0000-0000-0003-000000000001");
        var categoryId = new Guid("20000000-0000-0000-0001-000000000001");

        // Seed a partial database with edited names and rows outside the reference set.
        await new UnitOfWork(db).ExecuteInTransactionAsync(_ =>
        {
            db.Authors.Add(new Author(CatalogSeedData.Authors[0].Id, "Edited author"));
            db.Publishers.Add(new Publisher(CatalogSeedData.Publishers[0].Id, "Edited publisher"));
            var otherCategory = new Category(categoryId, "Other category");
            var editedCategory = new Category(CatalogSeedData.Categories[0].Id, "Edited category");
            editedCategory.SetParent(otherCategory);
            db.Categories.Add(editedCategory);
            db.Authors.Add(new Author(authorId, "Other author"));
            db.Publishers.Add(new Publisher(publisherId, "Other publisher"));
            db.Categories.Add(otherCategory);
            return Task.CompletedTask;
        });

        for (var run = 0; run < 2; run++)
        {
            db.ChangeTracker.Clear();
            await InitializeAsync(db);
            db.ChangeTracker.Clear();

            var authors = await db.Authors.ToDictionaryAsync(x => x.Id, x => x.FullName);
            var publishers = await db.Publishers.ToDictionaryAsync(x => x.Id, x => x.Name);
            var categories = await db.Categories.ToDictionaryAsync(x => x.Id, x => x.Name);
            Assert.Equal(76, authors.Count);
            Assert.Equal(28, publishers.Count);
            Assert.Equal(9, categories.Count);
            Assert.Equal("Edited author", authors[CatalogSeedData.Authors[0].Id]);
            Assert.Equal("Edited publisher", publishers[CatalogSeedData.Publishers[0].Id]);
            Assert.Equal("Edited category", categories[CatalogSeedData.Categories[0].Id]);
            Assert.Equal("Other author", authors[authorId]);
            Assert.Equal("Other publisher", publishers[publisherId]);
            Assert.Equal("Other category", categories[categoryId]);
            Assert.Equal(categoryId, (await db.Categories.SingleAsync(
                x => x.Id == CatalogSeedData.Categories[0].Id)).ParentCategoryId);
            foreach (var (id, name) in CatalogSeedData.Authors.Skip(1))
                Assert.Equal(name, authors[id]);
            foreach (var (id, name) in CatalogSeedData.Publishers.Skip(1))
                Assert.Equal(name, publishers[id]);
            foreach (var (id, name) in CatalogSeedData.Categories.Skip(1))
                Assert.Equal(name, categories[id]);
        }
    });

    private static async Task AssertReferencesAsync(LibraryDbContext db)
    {
        db.ChangeTracker.Clear();
        var authors = await db.Authors.ToDictionaryAsync(x => x.Id, x => x.FullName);
        var publishers = await db.Publishers.ToDictionaryAsync(x => x.Id, x => x.Name);
        var categories = await db.Categories.ToDictionaryAsync(x => x.Id);
        Assert.Equal(75, authors.Count);
        Assert.Equal(27, publishers.Count);
        Assert.Equal(8, categories.Count);
        Assert.Equal(75, authors.Values.Distinct().Count());
        Assert.Equal(27, publishers.Values.Distinct().Count());
        Assert.DoesNotContain("no publisher", publishers.Values);
        foreach (var (id, name) in CatalogSeedData.Authors)
            Assert.Equal(name, authors[id]);
        foreach (var (id, name) in CatalogSeedData.Publishers)
            Assert.Equal(name, publishers[id]);
        foreach (var (id, name) in CatalogSeedData.Categories)
        {
            Assert.Equal(name, categories[id].Name);
            Assert.Null(categories[id].ParentCategoryId);
        }
    }

    private static async Task InitializeAsync(LibraryDbContext db)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
            ["Seed:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddCatalogModule(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var seeders = scope.ServiceProvider.GetServices<IModuleSeeder>().ToArray();
        Assert.Single(seeders);
        Assert.IsType<CatalogSeeder>(seeders[0]);
        Assert.Equal(10, seeders[0].Order);
        Assert.Equal("Catalog", seeders[0].Module);
        var initializer = new DatabaseInitializer(db, new UnitOfWork(db), seeders, configuration,
            new FakeEnvironment(), NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();
    }

    private static async Task WithDatabaseAsync(Func<LibraryDbContext, Task> test)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(TestDatabase.NewConnectionString());
        await using var db = new LibraryDbContext(options.Options);
        try
        {
            await db.Database.MigrateAsync();
            await test(db);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LibraryApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
