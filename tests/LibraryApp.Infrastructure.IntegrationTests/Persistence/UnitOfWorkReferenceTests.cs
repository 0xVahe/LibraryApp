using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>A migrated database, so the foreign keys and CHECK constraints are the real ones.</summary>
public sealed class UnitOfWorkReferenceDatabase : IAsyncLifetime
{
    public string ConnectionString { get; } = TestDatabase.NewConnectionString();

    public LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(ConnectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }
}

/// <summary>
/// A foreign key violation is the same for every module: another request changed the data after the module's check.
/// </summary>
public class UnitOfWorkReferenceTests(UnitOfWorkReferenceDatabase database) : IClassFixture<UnitOfWorkReferenceDatabase>
{
    [Fact]
    public async Task Deleting_a_referenced_row_is_reference_in_use_and_nothing_is_deleted()
    {
        var publisherId = Guid.NewGuid();
        await using (var seed = database.NewContext())
        {
            var category = new Category(Guid.NewGuid(), "Reference category 1");
            seed.AddRange(category, new Publisher(publisherId, "Referenced publisher"),
                new Book(Guid.NewGuid(), "Referencing book", "9780134685991", 2020, "en", null, category.Id, publisherId));
            await seed.SaveChangesAsync();
        }

        await using var db = database.NewContext();
        var error = await Assert.ThrowsAsync<DomainException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
            db.Publishers.Remove(await db.Publishers.SingleAsync(x => x.Id == publisherId, ct))));

        Assert.Equal(CommonErrors.ReferenceInUse, error.Error);
        Assert.False(db.ChangeTracker.HasChanges());
        await using var verify = database.NewContext();
        Assert.True(await verify.Publishers.AnyAsync(x => x.Id == publisherId));
    }

    [Fact]
    public async Task A_row_pointing_to_a_missing_row_is_reference_missing_and_nothing_is_saved()
    {
        var bookId = Guid.NewGuid();
        await using var db = database.NewContext();

        var error = await Assert.ThrowsAsync<DomainException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(_ =>
        {
            db.Books.Add(new Book(bookId, "Orphan book", "9780132350884", 2020, "en", null, Guid.NewGuid(), null));
            return Task.CompletedTask;
        }));

        Assert.Equal(CommonErrors.ReferenceMissing, error.Error);
        await using var verify = database.NewContext();
        Assert.False(await verify.Books.AnyAsync(x => x.Id == bookId));
    }

    [Fact]
    public async Task A_check_constraint_violation_is_not_reported_as_a_reference_error()
    {
        var categoryId = Guid.NewGuid();
        await using (var seed = database.NewContext())
        {
            seed.StaffCategories.Add(new StaffCategory(categoryId, "Reference check category", 3, 21, 1));
            await seed.SaveChangesAsync();
        }

        await using var db = database.NewContext();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(async ct =>
        {
            // Bypasses the entity rules on purpose: only the database CHECK can refuse it.
            var category = await db.StaffCategories.SingleAsync(x => x.Id == categoryId, ct);
            db.Entry(category).Property(nameof(StaffCategory.MaxLoans)).CurrentValue = 0;
        }));

        Assert.Contains("CK_StaffCategories_MaxLoansPositive", error.InnerException!.Message);
        Assert.False(db.ChangeTracker.HasChanges());
    }
}
