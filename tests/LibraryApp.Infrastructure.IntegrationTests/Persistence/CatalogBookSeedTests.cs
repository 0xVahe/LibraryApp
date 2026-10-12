using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public sealed class CatalogBookSeedTests(CatalogReferenceFixture fixture) : IClassFixture<CatalogReferenceFixture>
{
    [Fact]
    public async Task Book_seed_is_complete_and_idempotent()
    {
        await ResetAsync();
        await SeedTwiceAsync();

        await using var verify = fixture.Open();
        var bookIds = CatalogSeedData.Books.Select(book => CatalogSeedIds.Book(book.Number)).ToArray();

        Assert.Equal(CatalogSeedData.Books.Count, await verify.Books.CountAsync(book => bookIds.Contains(book.Id)));
        Assert.Equal(CatalogSeedData.Authors.Count, await verify.Authors.CountAsync());
        Assert.Equal(CatalogSeedData.Publishers.Count, await verify.Publishers.CountAsync());
        Assert.Equal(bookIds.Length, bookIds.Distinct().Count());
        Assert.Equal(8, await verify.Books.Where(book => bookIds.Contains(book.Id)).Select(book => book.CategoryId).Distinct().CountAsync());
        Assert.Equal(CatalogSeedData.CopiesPerBook.Values.Sum(),
            await verify.BookCopies.CountAsync(copy => bookIds.Contains(copy.BookId)));

        foreach (var seedBook in CatalogSeedData.Books)
        {
            var book = await verify.Books.SingleAsync(item => item.Id == CatalogSeedIds.Book(seedBook.Number));
            Assert.Equal(seedBook.Isbn, book.Isbn);
            Assert.Equal(seedBook.Title, book.Title);
            Assert.Equal(seedBook.Language, book.Language);
            Assert.Equal(seedBook.Authors.Count, await verify.BookAuthors.CountAsync(link => link.BookId == book.Id));
        }
    }

    [Fact]
    public async Task Book_seed_preserves_an_existing_book_edit()
    {
        await ResetAsync();
        await SeedOnceAsync();

        await using (var db = fixture.Open())
        {
            await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
            {
                var book = await db.Books.SingleAsync(item => item.Id == CatalogSeedIds.Book(1), ct);
                book.Update("Edited seed title", book.Isbn, book.PublicationYear, book.Language,
                    book.Description, book.CategoryId, book.PublisherId);
            });
        }

        await SeedOnceAsync();

        await using var verify = fixture.Open();
        Assert.Equal("Edited seed title", (await verify.Books.SingleAsync(book => book.Id == CatalogSeedIds.Book(1))).Title);
    }

    [Fact]
    public async Task Every_seeded_book_has_the_expected_authors_and_publisher()
    {
        await ResetAsync();
        await SeedOnceAsync();

        await using var verify = fixture.Open();
        foreach (var seedBook in CatalogSeedData.Books)
        {
            var book = await verify.Books
                .SingleAsync(item => item.Id == CatalogSeedIds.Book(seedBook.Number));

            var authors = await (
                from link in verify.BookAuthors
                join author in verify.Authors on link.AuthorId equals author.Id
                where link.BookId == book.Id
                orderby author.FullName
                select author.FullName).ToListAsync();

            Assert.Equal(seedBook.Authors.OrderBy(name => name), authors);

            if (seedBook.Publisher is null)
            {
                Assert.Null(book.PublisherId);
            }
            else
            {
                Assert.Equal(seedBook.Publisher,
                    await verify.Publishers.Where(publisher => publisher.Id == book.PublisherId)
                        .Select(publisher => publisher.Name).SingleAsync());
            }
        }
    }

    [Fact]
    public async Task Seeded_books_have_unique_valid_isbns_and_stable_ids()
    {
        await ResetAsync();
        await SeedOnceAsync();

        await using var verify = fixture.Open();
        var books = await verify.Books
            .Where(book => CatalogSeedData.Books.Select(seed => CatalogSeedIds.Book(seed.Number)).Contains(book.Id))
            .Select(book => new { book.Id, book.Isbn })
            .ToListAsync();

        Assert.Equal(CatalogSeedData.Books.Count, books.Count);
        Assert.Equal(books.Count, books.Select(book => book.Isbn).Distinct().Count());
        Assert.All(books, book =>
        {
            Assert.Equal(13, book.Isbn.Length);
            Assert.All(book.Isbn, character => Assert.InRange(character, '0', '9'));
            Assert.True(IsValidIsbn13(book.Isbn));
        });
    }

    private async Task SeedOnceAsync()
    {
        await using var db = fixture.Open();
        await new UnitOfWork(db).ExecuteInTransactionAsync(ct => new CatalogSeeder(db).SeedAsync(ct));
    }

    private async Task ResetAsync()
    {
        await using var db = fixture.Open();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private async Task SeedTwiceAsync()
    {
        await SeedOnceAsync();
        await SeedOnceAsync();
    }

    private static bool IsValidIsbn13(string isbn)
    {
        int sum = isbn.Take(12).Select((digit, index) =>
            (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        int checkDigit = (10 - sum % 10) % 10;
        return checkDigit == isbn[12] - '0';
    }
}
