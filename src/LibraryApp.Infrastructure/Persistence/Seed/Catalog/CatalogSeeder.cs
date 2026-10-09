using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Seed.Catalog;

/// <summary>Seeds categories, authors, publishers and books (D3). Runs inside the seed runner's transaction; rows that already exist, edited ones included, are kept.</summary>
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
        var authorIds = CatalogSeedData.Authors
            .ToDictionary(x => x.Name, x => x.Id, StringComparer.Ordinal);
        var publisherIds = CatalogSeedData.Publishers
            .ToDictionary(x => x.Name, x => x.Id, StringComparer.Ordinal);

        foreach (var seedBook in CatalogSeedData.Books)
        {
            Guid bookId = CatalogSeedIds.Book(seedBook.Number);
            if (existingBookIds.Contains(bookId))
                continue;

            Guid categoryId = CatalogSeedIds.Category(seedBook.CategoryNumber);
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
