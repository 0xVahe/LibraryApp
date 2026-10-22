using LibraryApp.Application.Reporting.Options;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Reporting;

/// <summary>
/// REP-07 data, Waiting reservations per book: Clean Code 3 (copies: Available, OnLoan, UnderRepair, Lost, Withdrawn),
/// Refactoring 3 (no copies), another edition of Refactoring 3, Effective Java 1 (plus a ReadyForPickup, an Expired and
/// a Cancelled one), a deleted book 4; Domain-Driven Design has no reservation.
/// </summary>
public sealed class PurchaseCandidatesDatabase : IAsyncLifetime
{
    public static readonly Guid CleanCodeId = Guid.NewGuid();
    public static readonly Guid RefactoringId = Guid.NewGuid();
    public static readonly Guid RefactoringFirstEditionId = Guid.NewGuid();
    public static readonly Guid EffectiveJavaId = Guid.NewGuid();
    public static readonly Guid DeletedBookId = Guid.NewGuid();
    public static readonly Guid DomainDrivenDesignId = Guid.NewGuid();

    private static readonly DateTime CreatedAtUtc = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    public string ConnectionString { get; } = TestDatabase.NewDatabase();

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

        var departmentId = Guid.NewGuid();
        var staffCategoryId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        db.Departments.Add(new Department(departmentId, "Finance"));
        db.StaffCategories.Add(new StaffCategory(staffCategoryId, "Employee", 3, 21, 1));
        db.Categories.Add(new Category(categoryId, "Technology"));

        var deleted = new Book(DeletedBookId, "Old Book", "9780201633610", 1994, "en", null, categoryId, null);
        deleted.Delete(CreatedAtUtc);
        db.Books.AddRange(
            new Book(CleanCodeId, "Clean Code", "9780132350884", 2008, "en", null, categoryId, null),
            new Book(RefactoringId, "Refactoring", "9780134757599", 2018, "en", null, categoryId, null),
            new Book(RefactoringFirstEditionId, "Refactoring", "9780201485677", 1999, "en", null, categoryId, null),
            new Book(EffectiveJavaId, "Effective Java", "9780134685991", 2018, "en", null, categoryId, null),
            new Book(DomainDrivenDesignId, "Domain-Driven Design", "9780321125217", 2003, "en", null, categoryId, null),
            deleted);

        AddCopy(db, CleanCodeId, "PC-CLEAN-001", BookCopyStatus.Available);
        AddCopy(db, CleanCodeId, "PC-CLEAN-002", BookCopyStatus.OnLoan);
        AddCopy(db, CleanCodeId, "PC-CLEAN-003", BookCopyStatus.UnderRepair);
        AddCopy(db, CleanCodeId, "PC-CLEAN-004", BookCopyStatus.Lost);
        AddCopy(db, CleanCodeId, "PC-CLEAN-005", BookCopyStatus.Withdrawn);
        Guid javaCopy = AddCopy(db, EffectiveJavaId, "PC-JAVA-001", BookCopyStatus.Available);

        List<Guid> staff = Enumerable.Range(1, 5)
            .Select(number => NewStaff(db, departmentId, staffCategoryId, $"FIN-{number:000}"))
            .ToList();

        AddWaiting(db, CleanCodeId, staff.Take(3));
        AddWaiting(db, RefactoringId, staff.Take(3));
        AddWaiting(db, RefactoringFirstEditionId, staff.Take(3));
        AddWaiting(db, EffectiveJavaId, staff.Take(1));
        AddWaiting(db, DeletedBookId, staff.Take(4));
        // Not in the queue: ready for pickup, expired and cancelled reservations.
        var ready = Reservation.Create(Guid.NewGuid(), EffectiveJavaId, staff[1], CreatedAtUtc, 2);
        ready.MarkReady(javaCopy, CreatedAtUtc.AddDays(3));
        var expired = Reservation.Create(Guid.NewGuid(), EffectiveJavaId, staff[2], CreatedAtUtc, 3);
        expired.MarkReady(javaCopy, CreatedAtUtc.AddDays(1));
        expired.Expire(CreatedAtUtc.AddDays(2));
        var cancelled = Reservation.Create(Guid.NewGuid(), EffectiveJavaId, staff[4], CreatedAtUtc, 4);
        cancelled.Cancel();
        db.Reservations.AddRange(ready, expired, cancelled);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static Guid AddCopy(LibraryDbContext db, Guid bookId, string barcode, BookCopyStatus status)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, barcode, BookCopyCondition.Good, null);
        if (status != BookCopyStatus.Available)
            copy.ChangeStatus(status);
        db.BookCopies.Add(copy);
        return copy.Id;
    }

    private static void AddWaiting(LibraryDbContext db, Guid bookId, IEnumerable<Guid> staffIds)
    {
        int position = 1;
        foreach (Guid staffId in staffIds)
            db.Reservations.Add(Reservation.Create(Guid.NewGuid(), bookId, staffId, CreatedAtUtc, position++));
    }

    private static Guid NewStaff(LibraryDbContext db, Guid departmentId, Guid categoryId, string employeeId)
    {
        Guid id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id,
            UserName = id.ToString("N"),
            NormalizedUserName = id.ToString("N").ToUpperInvariant(),
            Email = $"{id:N}@example.com",
            NormalizedEmail = $"{id:N}@EXAMPLE.COM"
        });

        db.StaffMembers.Add(new StaffMember(
            id, id, employeeId, "Test", "Employee",
            $"{employeeId.ToLowerInvariant()}@example.com",
            departmentId, "Analyst", null, categoryId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        return id;
    }
}

public sealed class PurchaseCandidatesRepositoryTests(PurchaseCandidatesDatabase database)
    : IClassFixture<PurchaseCandidatesDatabase>
{
    [Fact]
    public async Task Books_with_the_longest_waiting_queues_first_and_a_tie_by_title_then_ISBN()
    {
        await using var db = database.NewContext();

        var books = await Repository(db, limit: 10).GetPurchaseCandidatesAsync();

        Assert.Equal(
            [
                PurchaseCandidatesDatabase.CleanCodeId,
                PurchaseCandidatesDatabase.RefactoringId,               // 9780134757599
                PurchaseCandidatesDatabase.RefactoringFirstEditionId,   // 9780201485677
                PurchaseCandidatesDatabase.EffectiveJavaId,
            ],
            books.Select(book => book.BookId));
        Assert.Equal([3, 3, 3, 1], books.Select(book => book.WaitingReservations));
    }

    [Fact]
    public async Task Only_Waiting_reservations_are_counted()
    {
        await using var db = database.NewContext();

        var books = await Repository(db, limit: 10).GetPurchaseCandidatesAsync();

        // Effective Java also has a ReadyForPickup, an Expired and a Cancelled reservation.
        Assert.Equal(1, books.Single(book => book.BookId == PurchaseCandidatesDatabase.EffectiveJavaId).WaitingReservations);
    }

    [Fact]
    public async Task Deleted_books_and_books_without_a_queue_are_left_out()
    {
        await using var db = database.NewContext();

        var books = await Repository(db, limit: 10).GetPurchaseCandidatesAsync();

        Assert.DoesNotContain(books, book => book.BookId == PurchaseCandidatesDatabase.DeletedBookId);
        Assert.DoesNotContain(books, book => book.BookId == PurchaseCandidatesDatabase.DomainDrivenDesignId);
    }

    [Fact]
    public async Task Copies_are_the_ones_the_library_still_has_with_the_book_details()
    {
        await using var db = database.NewContext();

        var books = await Repository(db, limit: 10).GetPurchaseCandidatesAsync();

        var cleanCode = books.Single(book => book.BookId == PurchaseCandidatesDatabase.CleanCodeId);
        Assert.Equal("Clean Code", cleanCode.Title);
        Assert.Equal("9780132350884", cleanCode.Isbn);
        Assert.Equal("Technology", cleanCode.Category);
        Assert.Equal(3, cleanCode.CopiesInCollection);   // Available, OnLoan and UnderRepair; not Lost or Withdrawn
        Assert.Equal(0, books.Single(book => book.BookId == PurchaseCandidatesDatabase.RefactoringId).CopiesInCollection);
    }

    [Fact]
    public async Task Respects_the_configured_limit()
    {
        await using var db = database.NewContext();

        var books = await Repository(db, limit: 1).GetPurchaseCandidatesAsync();

        Assert.Equal(PurchaseCandidatesDatabase.CleanCodeId, Assert.Single(books).BookId);
    }

    private static ReportingReadRepository Repository(LibraryDbContext db, int limit) =>
        new(db, Options.Create(new ReportingOptions
        {
            DashboardCacheMinutes = 5,
            TopBooksLimit = 10,
            TopCategoriesLimit = 5,
            PurchaseCandidatesLimit = limit
        }));
}
