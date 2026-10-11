using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>The copies part of the catalog seed (CAT-04, D3), on a real SQL Server database.</summary>
public sealed class CatalogCopySeedTests(CatalogReferenceFixture fixture) : IClassFixture<CatalogReferenceFixture>
{
    private static readonly int SeededCopies = CatalogSeedData.CopiesPerBook.Values.Sum();

    [Fact]
    public async Task Copy_seed_is_complete_and_idempotent()
    {
        await ResetAsync();
        await SeedAsync();
        await SeedAsync();

        await using var verify = fixture.Open();
        var copies = await verify.BookCopies.AsNoTracking().ToDictionaryAsync(copy => copy.Id);

        Assert.Equal(SeededCopies, copies.Count);
        foreach (var seedBook in CatalogSeedData.Books)
        {
            for (int copyNumber = 1; copyNumber <= CatalogSeedData.CopiesPerBook[seedBook.Number]; copyNumber++)
            {
                var copy = copies[CatalogSeedIds.Copy(seedBook.Number, copyNumber)];
                Assert.Equal(CatalogSeedIds.Book(seedBook.Number), copy.BookId);
                Assert.Equal(CatalogSeedIds.CopyBarcode(seedBook.Number, copyNumber), copy.Barcode);
                Assert.Equal(BookCopyStatus.Available, copy.Status);
                Assert.Equal(BookCopyCondition.Good, copy.Condition);
                Assert.Equal($"S{seedBook.CategoryNumber}-{seedBook.Number:D2}", copy.ShelfLocation);
            }
        }
    }

    [Fact]
    public async Task Copy_seed_keeps_an_existing_copy_edit()
    {
        await ResetAsync();
        await SeedAsync();
        Guid copyId = CatalogSeedIds.Copy(1, 1);

        await using (var db = fixture.Open())
        {
            await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
                (await db.BookCopies.SingleAsync(copy => copy.Id == copyId, ct)).ChangeStatus(BookCopyStatus.UnderRepair));
        }

        await SeedAsync();

        await using var verify = fixture.Open();
        Assert.Equal(BookCopyStatus.UnderRepair, (await verify.BookCopies.SingleAsync(copy => copy.Id == copyId)).Status);
        Assert.Equal(SeededCopies, await verify.BookCopies.CountAsync());
    }

    [Fact]
    public async Task A_barcode_already_taken_by_another_copy_is_skipped()
    {
        // An older database: copy 2 of book 1 was never seeded, and a librarian gave its barcode to another copy.
        await ResetAsync();
        await SeedAsync();
        Guid librarianCopyId = Guid.NewGuid();

        await using (var db = fixture.Open())
        {
            await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
            {
                db.BookCopies.Remove(await db.BookCopies.SingleAsync(copy => copy.Id == CatalogSeedIds.Copy(1, 2), ct));
            });
        }

        await using (var db = fixture.Open())
        {
            await new UnitOfWork(db).ExecuteInTransactionAsync(_ =>
            {
                db.BookCopies.Add(new BookCopy(
                    librarianCopyId, CatalogSeedIds.Book(1), CatalogSeedIds.CopyBarcode(1, 2), BookCopyCondition.New, null));
                return Task.CompletedTask;
            });
        }

        await SeedAsync();

        await using var verify = fixture.Open();
        Assert.False(await verify.BookCopies.AnyAsync(copy => copy.Id == CatalogSeedIds.Copy(1, 2)));
        Assert.True(await verify.BookCopies.AnyAsync(copy => copy.Id == librarianCopyId));
        Assert.Equal(SeededCopies, await verify.BookCopies.CountAsync());
    }

    [Fact]
    public async Task A_soft_deleted_book_gets_no_new_copies()
    {
        // An older database: book 1 has no copies yet and was deleted.
        await ResetAsync();
        await SeedAsync();
        Guid bookId = CatalogSeedIds.Book(1);

        await using (var db = fixture.Open())
        {
            await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
            {
                db.BookCopies.RemoveRange(await db.BookCopies.Where(copy => copy.BookId == bookId).ToListAsync(ct));
                (await db.Books.SingleAsync(book => book.Id == bookId, ct)).Delete(DateTime.UtcNow);
            });
        }

        await SeedAsync();

        await using var verify = fixture.Open();
        Assert.Equal(0, await verify.BookCopies.CountAsync(copy => copy.BookId == bookId));
        Assert.Equal(SeededCopies - CatalogSeedData.CopiesPerBook[1], await verify.BookCopies.CountAsync());
    }

    private async Task SeedAsync()
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
}
