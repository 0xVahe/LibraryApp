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
        await SeedReferenceAsync(ct);
        await SeedBooksAsync(ct);
    }

    private async Task SeedReferenceAsync(CancellationToken ct)
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

    private async Task SeedBooksAsync(CancellationToken ct)
    {
        var existingBookIds = (await db.Books.Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        var existingAuthors = await db.Authors.ToDictionaryAsync(x => x.FullName, x => x.Id, ct);
        var existingPublishers = await db.Publishers.ToDictionaryAsync(x => x.Name, x => x.Id, ct);

        var authorIds = CatalogSeedData.Books
            .SelectMany(book => book.Authors)
            .Distinct(StringComparer.Ordinal)
            .Select((name, index) => (name, id: CatalogSeedIds.Author(index + 1)))
            .ToDictionary(x => x.name, x => x.id, StringComparer.Ordinal);

        var publisherIds = CatalogSeedData.Books
            .Where(book => book.Publisher is not null)
            .Select(book => book.Publisher!)
            .Distinct(StringComparer.Ordinal)
            .Select((name, index) => (name, id: CatalogSeedIds.Publisher(index + 1)))
            .ToDictionary(x => x.name, x => x.id, StringComparer.Ordinal);

        foreach (var (name, id) in authorIds)
        {
            if (existingAuthors.ContainsKey(name))
            {
                authorIds[name] = existingAuthors[name];
                continue;
            }

            db.Authors.Add(new Author(id, name));
            existingAuthors[name] = id;
        }

        foreach (var (name, id) in publisherIds)
        {
            if (existingPublishers.ContainsKey(name))
            {
                publisherIds[name] = existingPublishers[name];
                continue;
            }

            db.Publishers.Add(new Publisher(id, name));
            existingPublishers[name] = id;
        }

        foreach (var seedBook in CatalogSeedData.Books)
        {
            Guid bookId = CatalogSeedIds.Book(seedBook.Number);
            if (existingBookIds.Contains(bookId))
                continue;

            Guid categoryId = CatalogSeedData.Categories[seedBook.CategoryNumber - 1].Id;
            Guid? publisherId = seedBook.Publisher is null ? null : publisherIds[seedBook.Publisher];
            var book = new Book(
                bookId,
                seedBook.Title,
                seedBook.Isbn,
                seedBook.PublicationYear,
                seedBook.Language,
                description: null,
                categoryId,
                publisherId);

            db.Books.Add(book);
            foreach (string author in seedBook.Authors)
                db.BookAuthors.Add(new BookAuthor(bookId, authorIds[author]));

            existingBookIds.Add(bookId);
        }
    }
}
