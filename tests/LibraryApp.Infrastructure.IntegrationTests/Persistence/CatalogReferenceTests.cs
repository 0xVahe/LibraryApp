using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public sealed class CatalogReferenceFixture : IAsyncLifetime
{
    private readonly string _connectionString = TestDatabase.NewConnectionString();
    public LibraryDbContext Open()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = Open();
        await db.Database.EnsureDeletedAsync();
    }
}

public class CatalogReferenceTests(CatalogReferenceFixture fixture) : IClassFixture<CatalogReferenceFixture>
{
    private static AuthorService Authors(LibraryDbContext db) => new(
        new AuthorRepository(db), new UnitOfWork(db), new SequentialGuidGenerator(TimeProvider.System), new SqlServerApplicationLock(db));

    private static CategoryService Categories(LibraryDbContext db) => new(
        new CategoryRepository(db), new UnitOfWork(db), new SequentialGuidGenerator(TimeProvider.System), new SqlServerApplicationLock(db));

    [Fact]
    public async Task Reference_seed_is_repeatable_and_preserves_existing_edits()
    {
        for (var run = 0; run < 2; run++)
        {
            await using var db = fixture.Open();
            await new UnitOfWork(db).ExecuteInTransactionAsync(ct => new CatalogSeeder(db).SeedAsync(ct));
        }

        await using var check = fixture.Open();
        var ids = CatalogSeedData.Categories.Select(x => x.Id).ToArray();
        var rows = await check.Categories.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        Assert.Equal(14, rows.Count);
        foreach (var (id, name, parentId) in CatalogSeedData.Categories)
        {
            Assert.Equal(name, rows[id].Name);
            Assert.Equal(parentId, rows[id].ParentCategoryId);
        }

        await new UnitOfWork(check).ExecuteInTransactionAsync(_ =>
        {
            rows[ids[0]].Rename("Edited category");
            return Task.CompletedTask;
        });
        await new UnitOfWork(check).ExecuteInTransactionAsync(ct => new CatalogSeeder(check).SeedAsync(ct));
        check.ChangeTracker.Clear();
        Assert.Equal("Edited category", (await check.Categories.SingleAsync(x => x.Id == ids[0])).Name);
        Assert.Equal(14, await check.Categories.CountAsync(x => ids.Contains(x.Id)));
    }

    [Fact]
    public async Task Author_replacement_persists_and_references_including_deleted_books_block_deletion()
    {
        await using var db = fixture.Open();
        var authors = Authors(db);
        var categories = Categories(db);
        var category = await categories.CreateAsync(new CategoryRequest("Book category", null));
        var first = await authors.CreateAsync(new AuthorRequest("First synthetic author"));
        var second = await authors.CreateAsync(new AuthorRequest("Second synthetic author"));
        var book = new Book(Guid.NewGuid(), "Synthetic book", "9780134685991", 2020, "en", null, category.Id, null);
        await new UnitOfWork(db).ExecuteInTransactionAsync(_ =>
        {
            db.Books.Add(book);
            db.BookAuthors.Add(new BookAuthor(book.Id, first.Id));
            return Task.CompletedTask;
        });

        await authors.SetBookAuthorsAsync(book.Id, new BookAuthorsRequest([first.Id, second.Id, second.Id]));
        await authors.SetBookAuthorsAsync(book.Id, new BookAuthorsRequest([second.Id]));
        db.ChangeTracker.Clear();
        Assert.Equal(second.Id, (await db.BookAuthors.SingleAsync(x => x.BookId == book.Id)).AuthorId);
        await authors.DeleteAsync(first.Id);
        var missing = await Assert.ThrowsAsync<DomainException>(() =>
            authors.SetBookAuthorsAsync(book.Id, new BookAuthorsRequest([Guid.NewGuid()])));
        Assert.Equal(CatalogErrors.AuthorNotFound, missing.Error);
        Assert.Equal(second.Id, (await db.BookAuthors.SingleAsync(x => x.BookId == book.Id)).AuthorId);

        await new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
            (await db.Books.SingleAsync(x => x.Id == book.Id, ct)).Delete(DateTime.UtcNow));
        var authorError = await Assert.ThrowsAsync<DomainException>(() => authors.DeleteAsync(second.Id));
        Assert.Equal(CatalogErrors.AuthorInUse, authorError.Error);
        var categoryError = await Assert.ThrowsAsync<DomainException>(() => categories.DeleteAsync(category.Id));
        Assert.Equal(CatalogErrors.CategoryInUse, categoryError.Error);
    }

    [Fact]
    public async Task Reparenting_loads_existing_children_from_the_database()
    {
        await using var db = fixture.Open();
        var service = Categories(db);
        var root = await service.CreateAsync(new CategoryRequest("Root", null));
        var other = await service.CreateAsync(new CategoryRequest("Other root", null));
        var child = await service.CreateAsync(new CategoryRequest("Child", root.Id));
        db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.UpdateAsync(root.Id, new CategoryRequest("Root", other.Id)));
        Assert.Equal(CatalogErrors.CategoryHasSubcategories, error.Error);
        Assert.Null((await service.GetAsync(root.Id)).ParentCategoryId);
        Assert.Equal(root.Id, (await service.GetAsync(child.Id)).ParentCategoryId);

        await service.UpdateAsync(child.Id, new CategoryRequest("Child", other.Id));
        db.ChangeTracker.Clear();
        Assert.Empty((await new CategoryRepository(db).FindAsync(root.Id, default))!.Children);
        Assert.Single((await new CategoryRepository(db).FindAsync(other.Id, default))!.Children);
    }

    [Fact]
    public async Task Concurrent_reparenting_cannot_create_three_levels()
    {
        await using var db = fixture.Open();
        var service = Categories(db);
        var first = await service.CreateAsync(new CategoryRequest("First root", null));
        var second = await service.CreateAsync(new CategoryRequest("Second root", null));
        var third = await service.CreateAsync(new CategoryRequest("Third root", null));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondContended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> Move(Guid id, Guid parent, bool holdLock)
        {
            await using var connection = fixture.Open();
            var coordinatedLock = new CoordinatedCategoryLock(connection, holdLock, firstLocked, secondContended);
            var categoryService = new CategoryService(new CategoryRepository(connection), new UnitOfWork(connection),
                new SequentialGuidGenerator(TimeProvider.System), coordinatedLock);
            return await Record.ExceptionAsync(() =>
                categoryService.UpdateAsync(id, new CategoryRequest("Moved", parent), timeout.Token));
        }

        var firstMove = Move(second.Id, first.Id, holdLock: true);
        Task<Exception?>? secondMove = null;
        try
        {
            await firstLocked.Task.WaitAsync(timeout.Token);
            secondMove = Move(third.Id, second.Id, holdLock: false);
            var results = await Task.WhenAll(firstMove, secondMove).WaitAsync(timeout.Token);
            Assert.Null(results[0]);
            Assert.Equal(CatalogErrors.CategoryParentIsNotTopLevel, Assert.IsType<DomainException>(results[1]).Error);
        }
        finally
        {
            await timeout.CancelAsync();
            await firstMove;
            if (secondMove is not null) await secondMove;
        }

        await using var verify = fixture.Open();
        var ids = new[] { first.Id, second.Id, third.Id };
        var parents = await verify.Categories.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.ParentCategoryId);
        Assert.Null(parents[first.Id]);
        Assert.Equal(first.Id, parents[second.Id]);
        Assert.Null(parents[third.Id]);
        foreach (var parentId in parents.Values.Where(x => x.HasValue))
            Assert.Null(parents[parentId!.Value]);
    }

    private sealed class CoordinatedCategoryLock(
        LibraryDbContext db, bool holdLock, TaskCompletionSource firstLocked, TaskCompletionSource secondContended) : IApplicationLock
    {
        public async Task AcquireAsync(string resource, CancellationToken ct = default)
        {
            Assert.Equal("catalog:categories", resource);
            var transaction = db.Database.CurrentTransaction;
            Assert.NotNull(transaction);

            if (holdLock)
            {
                await new SqlServerApplicationLock(db).AcquireAsync(resource, ct);
                firstLocked.TrySetResult();
                await secondContended.Task.WaitAsync(ct);
                return;
            }

            // Confirm actual contention from the second transaction before allowing the first to continue.
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT APPLOCK_TEST('public', @resource, 'Exclusive', 'Transaction')";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@resource";
            parameter.Value = resource;
            command.Parameters.Add(parameter);
            try
            {
                Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(ct)));
            }
            finally
            {
                secondContended.TrySetResult();
            }

            await new SqlServerApplicationLock(db).AcquireAsync(resource, ct);
        }
    }
}
