using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>Runs inside the seed runner's transaction; existing reference rows are preserved.</summary>
public sealed class CatalogSeeder(LibraryDbContext db) : IModuleSeeder
{
    public int Order => 10;
    public string Module => "Catalog";

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Categories.Select(x => x.Id).ToListAsync(ct);
        foreach (var (id, name) in CatalogSeedData.Categories.Where(x => !existing.Contains(x.Id)))
            db.Categories.Add(new Category(id, name));

        var existingAuthors = await db.Authors.Select(x => x.Id).ToListAsync(ct);
        foreach (var (id, name) in CatalogSeedData.Authors.Where(x => !existingAuthors.Contains(x.Id)))
            db.Authors.Add(new Author(id, name));

        var existingPublishers = await db.Publishers.Select(x => x.Id).ToListAsync(ct);
        foreach (var (id, name) in CatalogSeedData.Publishers.Where(x => !existingPublishers.Contains(x.Id)))
            db.Publishers.Add(new Publisher(id, name));
    }
}
