using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Reporting;

/// <summary>
/// REP-05 data: Technology with its subcategory Architecture, and Finance; one book in each. Technology: Available/Good,
/// OnLoan/New. Architecture: Available/Poor, Lost/Good. Finance: Available/Good, Withdrawn/Fair.
/// </summary>
public sealed class InventoryStatusDatabase : IAsyncLifetime
{
    public static readonly Guid TechnologyId = Guid.NewGuid();
    public static readonly Guid ArchitectureId = Guid.NewGuid();
    public static readonly Guid FinanceId = Guid.NewGuid();

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

        var technology = new Category(TechnologyId, "Technology");
        var architecture = new Category(ArchitectureId, "Architecture");
        architecture.SetParent(technology);
        db.Categories.AddRange(technology, architecture, new Category(FinanceId, "Finance"));

        Guid technologyBook = AddBook(db, "Effective Java", "9780134685991", TechnologyId);
        Guid architectureBook = AddBook(db, "Clean Architecture", "9780134494166", ArchitectureId);
        Guid financeBook = AddBook(db, "The Intelligent Investor", "9780060555665", FinanceId);

        AddCopy(db, technologyBook, BookCopyCondition.Good, BookCopyStatus.Available);
        AddCopy(db, technologyBook, BookCopyCondition.New, BookCopyStatus.OnLoan);
        AddCopy(db, architectureBook, BookCopyCondition.Poor, BookCopyStatus.Available);
        AddCopy(db, architectureBook, BookCopyCondition.Good, BookCopyStatus.Lost);
        AddCopy(db, financeBook, BookCopyCondition.Good, BookCopyStatus.Available);
        AddCopy(db, financeBook, BookCopyCondition.Fair, BookCopyStatus.Withdrawn);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static Guid AddBook(LibraryDbContext db, string title, string isbn, Guid categoryId)
    {
        Guid id = Guid.NewGuid();
        db.Books.Add(new Book(id, title, isbn, 2018, "en", null, categoryId, null));
        return id;
    }

    private static void AddCopy(LibraryDbContext db, Guid bookId, BookCopyCondition condition, BookCopyStatus status)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, $"INV-{Guid.NewGuid():N}"[..20], condition, null);
        if (status != BookCopyStatus.Available)
            copy.ChangeStatus(status);
        db.BookCopies.Add(copy);
    }
}

public sealed class InventoryStatusRepositoryTests(InventoryStatusDatabase database)
    : IClassFixture<InventoryStatusDatabase>
{
    [Fact]
    public async Task Counts_every_copy_of_the_library_per_status_and_condition()
    {
        await using var db = database.NewContext();

        var counts = await Repository(db).GetCopyCountsAsync(null);

        Assert.Equal(6, counts.Sum(row => row.Copies));
        Assert.Equal(2, CopiesOf(counts, BookCopyStatus.Available, BookCopyCondition.Good));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.Available, BookCopyCondition.Poor));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.OnLoan, BookCopyCondition.New));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.Lost, BookCopyCondition.Good));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.Withdrawn, BookCopyCondition.Fair));
    }

    [Fact]
    public async Task A_category_includes_its_subcategories()
    {
        await using var db = database.NewContext();

        var counts = await Repository(db).GetCopyCountsAsync(InventoryStatusDatabase.TechnologyId);

        Assert.Equal(4, counts.Sum(row => row.Copies));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.Available, BookCopyCondition.Good));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.Available, BookCopyCondition.Poor));
        Assert.Equal(0, CopiesOf(counts, BookCopyStatus.Withdrawn, BookCopyCondition.Fair));   // Finance
    }

    [Fact]
    public async Task A_subcategory_has_only_its_own_copies()
    {
        await using var db = database.NewContext();

        var counts = await Repository(db).GetCopyCountsAsync(InventoryStatusDatabase.ArchitectureId);

        Assert.Equal(2, counts.Sum(row => row.Copies));
        Assert.Equal(1, CopiesOf(counts, BookCopyStatus.Lost, BookCopyCondition.Good));
    }

    [Fact]
    public async Task Category_name_or_null_when_it_does_not_exist()
    {
        await using var db = database.NewContext();
        var repository = Repository(db);

        Assert.Equal("Architecture", await repository.GetCategoryNameAsync(InventoryStatusDatabase.ArchitectureId));
        Assert.Null(await repository.GetCategoryNameAsync(Guid.NewGuid()));
    }

    private static ReportingReadRepository Repository(LibraryDbContext db) =>
        new(db, Options.Create(new ReportingOptions
        {
            DashboardCacheMinutes = 5,
            TopBooksLimit = 10,
            TopCategoriesLimit = 5
        }));

    private static int CopiesOf(IEnumerable<CopyCountData> counts, BookCopyStatus status, BookCopyCondition condition) =>
        counts.SingleOrDefault(row => row.Status == status && row.Condition == condition)?.Copies ?? 0;
}
