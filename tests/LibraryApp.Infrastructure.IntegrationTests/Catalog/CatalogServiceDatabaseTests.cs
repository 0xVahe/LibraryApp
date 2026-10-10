using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Catalog;

/// <summary>A real SQL Server database for the Catalog contract (CAT-07, INT-01, INT-02). Each test uses its own copies.</summary>
public sealed class CatalogServiceDatabase : IAsyncLifetime
{
    public static readonly Guid CategoryId = new("c3000000-0000-0000-0000-000000000001");

    public static readonly Guid ShelfBookId = new("b3000000-0000-0000-0000-000000000001");     // copies in every status
    public static readonly Guid BorrowedBookId = new("b3000000-0000-0000-0000-000000000002");  // its only copy is OnLoan
    public static readonly Guid DeletedBookId = new("b3000000-0000-0000-0000-000000000003");   // soft-deleted, copy still Available
    public static readonly Guid EmptyBookId = new("b3000000-0000-0000-0000-000000000004");     // no copies
    public static readonly Guid StatusBookId = new("b3000000-0000-0000-0000-000000000005");    // copies the status tests change

    public static readonly Guid FirstAvailableCopyId = new("e3000000-0000-0000-0000-000000000001");  // PP-1
    public static readonly Guid SecondAvailableCopyId = new("e3000000-0000-0000-0000-000000000002"); // PP-2
    public static readonly Guid OnLoanCopyId = new("e3000000-0000-0000-0000-000000000003");          // PP-3
    public static readonly Guid LostCopyId = new("e3000000-0000-0000-0000-000000000004");            // PP-4
    public static readonly Guid WithdrawnCopyId = new("e3000000-0000-0000-0000-000000000005");       // PP-5
    public static readonly Guid DeletedBookCopyId = new("e3000000-0000-0000-0000-000000000006");     // OLD-1
    public static readonly Guid StatusCopyId = new("e3000000-0000-0000-0000-000000000007");          // ST-1
    public static readonly Guid ConflictCopyId = new("e3000000-0000-0000-0000-000000000008");        // ST-2

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
        await db.Database.EnsureCreatedAsync();

        db.Categories.Add(new Category(CategoryId, "IT & Software Engineering"));

        db.Books.Add(new Book(ShelfBookId, "The Pragmatic Programmer", "9781000000177", 2019, "en", null, CategoryId, null));
        db.Books.Add(new Book(BorrowedBookId, "Refactoring", "9781000000191", 2018, "en", null, CategoryId, null));
        var deleted = new Book(DeletedBookId, "Old Edition", "9781000000207", 2003, "en", null, CategoryId, null);
        deleted.Delete(DateTime.UtcNow);
        db.Books.Add(deleted);
        db.Books.Add(new Book(EmptyBookId, "Clean Code", "9781000000153", 2008, "en", null, CategoryId, null));
        db.Books.Add(new Book(StatusBookId, "Clean Architecture", "9781000000160", 2017, "en", null, CategoryId, null));

        // PP-2 is added before PP-1: the available copy is chosen by barcode, not by insert order.
        db.BookCopies.AddRange(
            Copy(SecondAvailableCopyId, ShelfBookId, "PP-2", BookCopyStatus.Available),
            Copy(FirstAvailableCopyId, ShelfBookId, "PP-1", BookCopyStatus.Available),
            Copy(OnLoanCopyId, ShelfBookId, "PP-3", BookCopyStatus.OnLoan),
            Copy(LostCopyId, ShelfBookId, "PP-4", BookCopyStatus.Lost),
            Copy(WithdrawnCopyId, ShelfBookId, "PP-5", BookCopyStatus.Withdrawn),
            Copy(Guid.NewGuid(), BorrowedBookId, "BR-1", BookCopyStatus.OnLoan),
            Copy(DeletedBookCopyId, DeletedBookId, "OLD-1", BookCopyStatus.Available),
            Copy(StatusCopyId, StatusBookId, "ST-1", BookCopyStatus.Available),
            Copy(ConflictCopyId, StatusBookId, "ST-2", BookCopyStatus.Available));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static BookCopy Copy(Guid id, Guid bookId, string barcode, BookCopyStatus status)
    {
        var copy = new BookCopy(id, bookId, barcode, BookCopyCondition.Good, null);
        if (status != BookCopyStatus.Available)
            copy.ChangeStatus(status);
        return copy;
    }
}

public class CatalogServiceDatabaseTests(CatalogServiceDatabase database) : IClassFixture<CatalogServiceDatabase>
{
    // ---------- GetAvailableCopyAsync (INT-01) ----------

    [Fact]
    public async Task The_available_copy_is_the_one_with_the_smallest_barcode()
    {
        await using var db = database.NewContext();

        var copy = await Service(db).GetAvailableCopyAsync(CatalogServiceDatabase.ShelfBookId);

        Assert.NotNull(copy);
        Assert.Equal(CatalogServiceDatabase.FirstAvailableCopyId, copy.Id);
        Assert.Equal(CatalogServiceDatabase.ShelfBookId, copy.BookId);
        Assert.Equal("PP-1", copy.Barcode);
        Assert.Equal(BookCopyStatus.Available, copy.Status);
    }

    [Fact]
    public async Task No_copy_is_offered_for_a_borrowed_deleted_empty_or_unknown_book()
    {
        await using var db = database.NewContext();
        var service = Service(db);

        Assert.Null(await service.GetAvailableCopyAsync(CatalogServiceDatabase.BorrowedBookId));
        Assert.Null(await service.GetAvailableCopyAsync(CatalogServiceDatabase.DeletedBookId));
        Assert.Null(await service.GetAvailableCopyAsync(CatalogServiceDatabase.EmptyBookId));
        Assert.Null(await service.GetAvailableCopyAsync(Guid.NewGuid()));
    }

    // ---------- GetCopyByBarcodeAsync (BOR-01) ----------

    [Fact]
    public async Task A_copy_is_found_by_a_scanned_barcode_whatever_its_status()
    {
        await using var db = database.NewContext();
        var service = Service(db);

        var lost = await service.GetCopyByBarcodeAsync(" pp-4 ");

        Assert.NotNull(lost);
        Assert.Equal(CatalogServiceDatabase.LostCopyId, lost.Id);
        Assert.Equal(BookCopyStatus.Lost, lost.Status);
        Assert.Null(await service.GetCopyByBarcodeAsync("NO-SUCH-COPY"));
    }

    // ---------- GetCopiesAsync, GetCopyIdsAsync ----------

    [Fact]
    public async Task Copies_come_once_each_and_unknown_ids_are_left_out()
    {
        await using var db = database.NewContext();

        var copies = await Service(db).GetCopiesAsync(
        [
            CatalogServiceDatabase.LostCopyId,
            CatalogServiceDatabase.FirstAvailableCopyId,
            CatalogServiceDatabase.LostCopyId,
            Guid.NewGuid(),
        ]);

        Assert.Equal(new[] { "PP-1", "PP-4" }, copies.Select(c => c.Barcode));
    }

    [Fact]
    public async Task All_copy_ids_of_a_book_are_returned_retired_and_deleted_ones_included()
    {
        await using var db = database.NewContext();
        var service = Service(db);

        Assert.Equal(
            new[]
            {
                CatalogServiceDatabase.FirstAvailableCopyId, CatalogServiceDatabase.SecondAvailableCopyId,
                CatalogServiceDatabase.OnLoanCopyId, CatalogServiceDatabase.LostCopyId, CatalogServiceDatabase.WithdrawnCopyId,
            },
            await service.GetCopyIdsAsync(CatalogServiceDatabase.ShelfBookId));
        Assert.Equal(new[] { CatalogServiceDatabase.DeletedBookCopyId }, await service.GetCopyIdsAsync(CatalogServiceDatabase.DeletedBookId));
        Assert.Empty(await service.GetCopyIdsAsync(CatalogServiceDatabase.EmptyBookId));
        Assert.Empty(await service.GetCopyIdsAsync(Guid.NewGuid()));
    }

    // ---------- GetBookAsync (CAT-07) ----------

    [Fact]
    public async Task A_book_comes_with_its_copy_counts_and_a_deleted_book_is_still_found()
    {
        await using var db = database.NewContext();
        var service = Service(db);

        var shelf = await service.GetBookAsync(CatalogServiceDatabase.ShelfBookId);
        var deleted = await service.GetBookAsync(CatalogServiceDatabase.DeletedBookId);

        Assert.NotNull(shelf);
        Assert.Equal(3, shelf.TotalCopies);          // Lost and Withdrawn are not counted
        Assert.Equal(2, shelf.AvailableCopies);
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.Null(await service.GetBookAsync(Guid.NewGuid()));
    }

    // ---------- SetCopyStatusAsync (INT-02, BR-BOR-4) ----------

    [Fact]
    public async Task A_status_change_is_saved_by_the_callers_unit_of_work()
    {
        await using (var db = database.NewContext())
        {
            await new UnitOfWork(db).ExecuteInTransactionAsync(ct =>
                Service(db).SetCopyStatusAsync(CatalogServiceDatabase.StatusCopyId, BookCopyStatus.OnLoan, ct));
        }

        Assert.Equal(BookCopyStatus.OnLoan, await StatusOfAsync(CatalogServiceDatabase.StatusCopyId));
    }

    [Fact]
    public async Task An_illegal_status_change_is_refused_and_nothing_is_saved()
    {
        await using var db = database.NewContext();

        var error = await Assert.ThrowsAsync<DomainException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(ct =>
            Service(db).SetCopyStatusAsync(CatalogServiceDatabase.LostCopyId, BookCopyStatus.Available, ct)));

        Assert.Equal(CatalogErrors.CopyStatusChangeNotAllowed(BookCopyStatus.Lost, BookCopyStatus.Available), error.Error);
        Assert.Equal(BookCopyStatus.Lost, await StatusOfAsync(CatalogServiceDatabase.LostCopyId));
    }

    [Fact]
    public async Task A_status_change_of_an_unknown_copy_is_refused()
    {
        await using var db = database.NewContext();

        var error = await Assert.ThrowsAsync<DomainException>(() => new UnitOfWork(db).ExecuteInTransactionAsync(ct =>
            Service(db).SetCopyStatusAsync(Guid.NewGuid(), BookCopyStatus.OnLoan, ct)));

        Assert.Equal(CatalogErrors.CopyNotFound, error.Error);
    }

    [Fact]
    public async Task Two_parallel_changes_of_the_same_copy_end_in_a_conflict()
    {
        var copyLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherChangeSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The slow request loads the copy (with its RowVersion), then waits before saving.
        await using var slow = database.NewContext();
        Task slowChange = new UnitOfWork(slow).ExecuteInTransactionAsync(async ct =>
        {
            await Service(slow).SetCopyStatusAsync(CatalogServiceDatabase.ConflictCopyId, BookCopyStatus.Reserved, ct);
            copyLoaded.SetResult();
            await otherChangeSaved.Task;
        });

        // If the slow request fails before loading the copy, stop here instead of waiting forever.
        await Task.WhenAny(copyLoaded.Task, slowChange);
        Assert.True(copyLoaded.Task.IsCompleted, "The slow request failed before it loaded the copy.");

        // Meanwhile another request changes the same copy and saves first.
        try
        {
            await using var fast = database.NewContext();
            await new UnitOfWork(fast).ExecuteInTransactionAsync(ct =>
                Service(fast).SetCopyStatusAsync(CatalogServiceDatabase.ConflictCopyId, BookCopyStatus.OnLoan, ct));
        }
        finally
        {
            otherChangeSaved.TrySetResult();   // always let the slow request finish, even if this part failed
        }

        var error = await Assert.ThrowsAsync<DomainException>(() => slowChange);
        Assert.Equal(CommonErrors.ConcurrencyConflict, error.Error);
        Assert.Equal(BookCopyStatus.OnLoan, await StatusOfAsync(CatalogServiceDatabase.ConflictCopyId));
    }

    private async Task<BookCopyStatus> StatusOfAsync(Guid copyId)
    {
        await using var db = database.NewContext();
        return await db.BookCopies.Where(c => c.Id == copyId).Select(c => c.Status).SingleAsync();
    }

    private static CatalogService Service(LibraryDbContext db) =>
        new(new CatalogCopyQuery(db), new BookCopyRepository(db), new BookDetailsQuery(db));
}

/// <summary>Other modules get the real service, scoped like the DbContext it shares with them. No database needed.</summary>
public class CatalogServiceRegistrationTests
{
    [Fact]
    public void Other_modules_get_the_real_scoped_catalog_service()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LibraryDbContext>(options => options.UseLibrarySqlServer("Server=unused;Database=unused"));
        services.AddCatalogModule(new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ICatalogService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<CatalogService>(scope.ServiceProvider.GetRequiredService<ICatalogService>());
    }
}
