using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Reporting;

/// <summary>
/// REP-04 data: Finance has an active, a restricted and a deactivated employee; IT has one active employee; Legal has
/// nobody. October loans: Finance 2 x Risk + 1 x Technology, IT 1 x Technology; one IT loan in September.
/// </summary>
public sealed class ReadingActivityDatabase : IAsyncLifetime
{
    public static readonly Guid FinanceId = Guid.NewGuid();
    public static readonly Guid ItId = Guid.NewGuid();
    public static readonly Guid LegalId = Guid.NewGuid();
    public static readonly Guid RiskCategoryId = Guid.NewGuid();
    public static readonly Guid TechnologyCategoryId = Guid.NewGuid();

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

        var staffCategoryId = Guid.NewGuid();
        db.StaffCategories.Add(new StaffCategory(staffCategoryId, "Employee", 3, 21, 1));

        db.Departments.AddRange(
            new Department(FinanceId, "Finance"),
            new Department(ItId, "IT"),
            new Department(LegalId, "Legal"));

        db.Categories.AddRange(
            new Category(RiskCategoryId, "Risk"),
            new Category(TechnologyCategoryId, "Technology"));

        Guid riskBookId = Guid.NewGuid();
        Guid technologyBookId = Guid.NewGuid();
        db.Books.AddRange(
            new Book(riskBookId, "Risk Basics", "9780132350884", 2008, "en", null, RiskCategoryId, null),
            new Book(technologyBookId, "Effective Java", "9780134685991", 2018, "en", null, TechnologyCategoryId, null));

        StaffMember financeActive = NewStaff(db, FinanceId, staffCategoryId, "FIN-001");
        StaffMember financeRestricted = NewStaff(db, FinanceId, staffCategoryId, "FIN-002");
        StaffMember financeLeft = NewStaff(db, FinanceId, staffCategoryId, "FIN-003");
        StaffMember itActive = NewStaff(db, ItId, staffCategoryId, "IT-001");
        financeRestricted.ChangeStatus(StaffMemberStatus.Restricted);
        financeLeft.Deactivate();

        db.Loans.AddRange(
            NewLoan(db, riskBookId, financeActive, new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc)),
            NewLoan(db, riskBookId, financeLeft, new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc)),
            NewLoan(db, technologyBookId, financeRestricted, new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc)),
            NewLoan(db, technologyBookId, itActive, new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc)),
            NewLoan(db, technologyBookId, itActive, new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc)));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static StaffMember NewStaff(LibraryDbContext db, Guid departmentId, Guid categoryId, string employeeId)
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

        var staff = new StaffMember(
            id, id, employeeId, "Test", "Employee",
            $"{employeeId.ToLowerInvariant()}@example.com",
            departmentId, "Analyst", null, categoryId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        db.StaffMembers.Add(staff);
        return staff;
    }

    private static Loan NewLoan(LibraryDbContext db, Guid bookId, StaffMember staff, DateTime issuedAtUtc)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, $"RA-{Guid.NewGuid():N}"[..20], BookCopyCondition.Good, null);
        db.BookCopies.Add(copy);
        return Loan.Issue(Guid.NewGuid(), copy.Id, staff.Id, issuedAtUtc, 21);
    }
}

public sealed class ReadingActivityRepositoryTests(ReadingActivityDatabase database)
    : IClassFixture<ReadingActivityDatabase>
{
    private static readonly DateTime OctoberStartUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OctoberEndUtc = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    [Fact]
    public async Task Lists_every_department_with_employees_who_have_not_left()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = (await repository.GetDepartmentEmployeesAsync()).OrderBy(row => row.Department).ToList();

        Assert.Equal(["Finance", "IT", "Legal"], rows.Select(row => row.Department));
        Assert.Equal([2, 1, 0], rows.Select(row => row.Employees));   // the deactivated Finance employee is not counted
    }

    [Fact]
    public async Task Counts_loans_in_the_period_per_department_and_category()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db);

        var rows = await repository.GetDepartmentCategoryLoansAsync(OctoberStartUtc, OctoberEndUtc);

        Assert.Equal(3, rows.Count);
        Assert.Equal(2, LoansOf(rows, ReadingActivityDatabase.FinanceId, ReadingActivityDatabase.RiskCategoryId));
        Assert.Equal(1, LoansOf(rows, ReadingActivityDatabase.FinanceId, ReadingActivityDatabase.TechnologyCategoryId));
        Assert.Equal(1, LoansOf(rows, ReadingActivityDatabase.ItId, ReadingActivityDatabase.TechnologyCategoryId));
        Assert.DoesNotContain(rows, row => row.DepartmentId == ReadingActivityDatabase.LegalId);
    }

    private static int LoansOf(
        IReadOnlyList<Application.Reporting.Ports.DepartmentCategoryLoansData> rows, Guid departmentId, Guid categoryId) =>
        Assert.Single(rows, row => row.DepartmentId == departmentId && row.CategoryId == categoryId).LoanCount;
}