
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Reporting;

public sealed class ReportingDatabase : IAsyncLifetime
{
    public static readonly Guid EngineeringId = Guid.NewGuid();
    public static readonly Guid FinanceId = Guid.NewGuid();
    public static readonly Guid EngineeringStaffId = Guid.NewGuid();
    public static readonly Guid FinanceStaffId = Guid.NewGuid();
    public static readonly Guid BookId = Guid.NewGuid();

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

        var staffCategoryId = Guid.NewGuid();
        var bookCategoryId = Guid.NewGuid();

        db.Departments.AddRange(
            new Department(EngineeringId, "Engineering"),
            new Department(FinanceId, "Finance"));

        db.StaffCategories.Add(
            new StaffCategory(staffCategoryId, "Employee", 3, 21, 1));

        db.Categories.Add(new Category(bookCategoryId, "Technology"));

        db.Users.AddRange(
            NewUser(EngineeringStaffId),
            NewUser(FinanceStaffId));

        db.StaffMembers.AddRange(
            NewStaff(EngineeringStaffId, EngineeringId, staffCategoryId, "ENG-001"),
            NewStaff(FinanceStaffId, FinanceId, staffCategoryId, "FIN-001"));

        db.Books.Add(new Book(
            BookId, "Clean Code", "9780132350884", 2008, "en",
            null, bookCategoryId, null));

        var copy1 = Guid.NewGuid();
        var copy2 = Guid.NewGuid();
        var copy3 = Guid.NewGuid();

        db.BookCopies.AddRange(
            new BookCopy(copy1, BookId, "REP-001", BookCopyCondition.Good, null),
            new BookCopy(copy2, BookId, "REP-002", BookCopyCondition.Good, null),
            new BookCopy(copy3, BookId, "REP-003", BookCopyCondition.Good, null));

        var first = Loan.Issue(
            Guid.NewGuid(), copy1, EngineeringStaffId,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 21);

        var second = Loan.Issue(
            Guid.NewGuid(), copy2, FinanceStaffId,
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 21);

        var third = Loan.Issue(
            Guid.NewGuid(), copy3, EngineeringStaffId,
            new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), 21);

        third.Return(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));

        db.Loans.AddRange(first, second, third);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static AppUser NewUser(Guid id) => new()
    {
        Id = id,
        UserName = id.ToString("N"),
        NormalizedUserName = id.ToString("N").ToUpperInvariant(),
        Email = $"{id:N}@example.com",
        NormalizedEmail = $"{id:N}@EXAMPLE.COM"
    };

    private static StaffMember NewStaff(
        Guid id, Guid departmentId, Guid categoryId, string employeeId) =>
        new(
            id, id, employeeId, "Test", "Employee",
            $"{employeeId.ToLowerInvariant()}@example.com",
            departmentId, "Developer", null, categoryId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}

public sealed class ReportingReadRepositoryTests(ReportingDatabase database)
    : IClassFixture<ReportingDatabase>
{
    private static readonly DateTime Cutoff =
        new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Overdue_report_excludes_returned_loans()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetOverdueLoansAsync(Cutoff, null);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.DueAtUtc < Cutoff));
    }

    [Fact]
    public async Task Overdue_report_filters_department_in_database()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetOverdueLoansAsync(
            Cutoff, ReportingDatabase.EngineeringId);

        var row = Assert.Single(rows);
        Assert.Equal(ReportingDatabase.EngineeringId, row.DepartmentId);
        Assert.Equal("ENG-001", row.EmployeeId);
        Assert.Equal("Engineering", row.Department);
        Assert.Equal("Clean Code", row.BookTitle);
    }

    [Fact]
    public async Task Overdue_report_returns_empty_for_unknown_department()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetOverdueLoansAsync(Cutoff, Guid.NewGuid());

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Dashboard_counts_books_copies_and_loans()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var counts = await repository.GetDashboardCountsAsync(Cutoff);

        Assert.Equal(1, counts.TotalBooks);
        Assert.Equal(3, counts.TotalCopies);
        Assert.Equal(2, counts.ActiveReaders);
        Assert.Equal(2, counts.ActiveLoans);
        Assert.Equal(3, counts.TotalLoans);
        Assert.Equal(2, counts.OverdueLoans);
        Assert.Equal(0, counts.RestrictedEmployees);
        Assert.Equal(0, counts.SuspendedEmployees);
    }

    [Fact]
    public async Task Top_books_count_loans_in_requested_period()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetTopBooksAsync(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc));

        var row = Assert.Single(rows);
        Assert.Equal(ReportingDatabase.BookId, row.BookId);
        Assert.Equal(3, row.LoanCount);
    }

    [Fact]
    public async Task Top_categories_count_loans_in_requested_period()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetTopCategoriesAsync(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc));

        var row = Assert.Single(rows);
        Assert.Equal("Technology", row.Name);
        Assert.Equal(3, row.LoanCount);
    }

    [Fact]
    public async Task Top_books_return_empty_outside_requested_period()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetTopBooksAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        Assert.Empty(rows);
    }
}
