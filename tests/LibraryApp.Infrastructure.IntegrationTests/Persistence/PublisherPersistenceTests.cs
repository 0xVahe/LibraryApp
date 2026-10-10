using System.Data.SqlTypes;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public class PublisherModelTests
{
    [Fact]
    public void Publisher_mapping_keeps_name_limit_nonunique_names_and_restrict_without_a_migration()
    {
        using var db = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options);
        var publisher = db.Model.FindEntityType(typeof(Publisher))!;
        var name = publisher.FindProperty(nameof(Publisher.Name))!;
        Assert.Equal(200, name.GetMaxLength());
        Assert.False(name.IsNullable);
        Assert.DoesNotContain(publisher.GetIndexes(), x => x.IsUnique && x.Properties.Contains(name));
        var foreignKey = db.Model.FindEntityType(typeof(Book))!.GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(Publisher));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

public class PublisherPersistenceTests(CatalogReferenceFixture fixture) : IClassFixture<CatalogReferenceFixture>
{
    private static PublisherService Service(LibraryDbContext db, IPublisherRepository? repository = null) => new(
        repository ?? new PublisherRepository(db), new UnitOfWork(db),
        new SequentialGuidGenerator(TimeProvider.System), new SqlServerApplicationLock(db));

    [Fact]
    public async Task Crud_persists_and_collection_is_ordered_without_tracking()
    {
        await using var db = fixture.Open();
        var service = Service(db);
        var last = await service.CreateAsync(new PublisherRequest("Zulu"));
        var first = await service.CreateAsync(new PublisherRequest("Alpha"));
        var duplicate = await service.CreateAsync(new PublisherRequest("Alpha"));
        await service.UpdateAsync(last.Id, new PublisherRequest("  Updated  "));
        db.ChangeTracker.Clear();

        var ids = new[] { last.Id, first.Id, duplicate.Id };
        var rows = (await service.ListAsync()).Where(x => ids.Contains(x.Id)).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(rows.OrderBy(x => x.Name).ThenBy(x => new SqlGuid(x.Id)), rows);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal("Updated", (await service.GetAsync(last.Id)).Name);
        await service.DeleteAsync(first.Id);
        db.ChangeTracker.Clear();
        Assert.False(await db.Publishers.AnyAsync(x => x.Id == first.Id));

        var foreignKey = db.Model.FindEntityType(typeof(Book))!.GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(Publisher));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Usage_check_counts_active_and_soft_deleted_books(bool deleted)
    {
        await using var db = fixture.Open();
        var service = Service(db);
        var publisher = await service.CreateAsync(new PublisherRequest("Referenced Publisher"));
        await AddBook(publisher.Id, deleted ? "9780201633610" : "9780134494166", deleted);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.DeleteAsync(publisher.Id));
        Assert.Equal(CatalogErrors.PublisherInUse, error.Error);
        Assert.Null(error.InnerException);
        Assert.True(await db.Publishers.AnyAsync(x => x.Id == publisher.Id));
    }

    [Fact]
    public async Task A_concurrent_book_reference_is_translated_to_conflict_and_transaction_rolls_back()
    {
        await using var db = fixture.Open();
        var publisher = await Service(db).CreateAsync(new PublisherRequest("Concurrent Publisher"));
        var repository = new ConcurrentReferenceRepository(new PublisherRepository(db),
            () => AddBook(publisher.Id, "9780132350884", false));
        var service = Service(db, repository);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.DeleteAsync(publisher.Id));
        Assert.Equal(CatalogErrors.PublisherInUse, error.Error);
        var update = Assert.IsType<DbUpdateException>(error.InnerException);
        Assert.Equal(547, Assert.IsType<SqlException>(update.InnerException).Number);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.True(await db.Publishers.AnyAsync(x => x.Id == publisher.Id));
        Assert.True(await db.Books.AnyAsync(x => x.PublisherId == publisher.Id));

        // A failed delete leaves the same scoped context usable for the next operation.
        await service.UpdateAsync(publisher.Id, new PublisherRequest("Still present"));
        Assert.Equal("Still present", (await service.GetAsync(publisher.Id)).Name);
    }

    [Fact]
    public async Task Missing_delete_returns_not_found()
    {
        await using var db = fixture.Open();
        var error = await Assert.ThrowsAsync<DomainException>(() => Service(db).DeleteAsync(Guid.NewGuid()));
        Assert.Equal(CatalogErrors.PublisherNotFound, error.Error);
    }

    private async Task AddBook(Guid publisherId, string isbn, bool deleted)
    {
        await using var db = fixture.Open();
        var category = new Category(Guid.NewGuid(), "Synthetic category");
        var book = new Book(Guid.NewGuid(), "Synthetic book", isbn, 2020, "en", null, category.Id, publisherId);
        if (deleted) book.Delete(DateTime.UtcNow);
        db.AddRange(category, book);
        await db.SaveChangesAsync();
    }

    private sealed class ConcurrentReferenceRepository(IPublisherRepository inner, Func<Task> addBook) : IPublisherRepository
    {
        public Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<Publisher?> FindAsync(Guid id, CancellationToken ct) => inner.FindAsync(id, ct);
        public void Add(Publisher publisher) => inner.Add(publisher);
        public void Remove(Publisher publisher) => inner.Remove(publisher);

        public async Task<bool> IsInUseAsync(Guid id, CancellationToken ct)
        {
            var used = await inner.IsInUseAsync(id, ct);
            Assert.False(used);
            // Commit on a separate connection after the check and before the publisher delete is saved.
            await addBook();
            return used;
        }
    }
}
