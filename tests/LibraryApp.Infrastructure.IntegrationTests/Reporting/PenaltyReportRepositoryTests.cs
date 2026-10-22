using LibraryApp.Application.Reporting.Options;
using LibraryApp.Application.Reporting.Ports;
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
/// REP-06 data, now is 20 Oct. October penalties: Finance 3 (active), 2 (waived), 4 (past its expiry), 1 (expired);
/// IT 5 (active). One IT penalty from September. Legal has none.
/// </summary>
public sealed class PenaltyReportDatabase : IAsyncLifetime
{
    public static readonly Guid FinanceId = Guid.NewGuid();
    public static readonly Guid ItId = Guid.NewGuid();
    public static readonly Guid LegalId = Guid.NewGuid();
    public static readonly DateTime NowUtc = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

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
        db.StaffCategories.Add(new StaffCategory(staffCategoryId, "Employee", 3, 21, 1));

        db.Departments.AddRange(
            new Department(FinanceId, "Finance"),
            new Department(ItId, "IT"),
            new Department(LegalId, "Legal"));

        var categoryId = Guid.NewGuid();
        db.Categories.Add(new Category(categoryId, "Risk"));
        var bookId = Guid.NewGuid();
        db.Books.Add(new Book(bookId, "Risk Basics", "9780132350884", 2008, "en", null, categoryId, null));

        StaffMember finance = NewStaff(db, FinanceId, staffCategoryId, "FIN-001");
        StaffMember it = NewStaff(db, ItId, staffCategoryId, "IT-001");

        Penalty(db, bookId, finance, 3, Utc(10, 2), Utc(12, 31));

        PenaltyRecord waived = Penalty(db, bookId, finance, 2, Utc(10, 3), Utc(12, 31));
        waived.Waive("Book returned damaged by courier");

        Penalty(db, bookId, finance, 4, Utc(10, 4), Utc(10, 10));

        PenaltyRecord expired = Penalty(db, bookId, finance, 1, Utc(10, 5), Utc(10, 6));
        expired.Expire(Utc(10, 7));

        Penalty(db, bookId, it, 5, Utc(10, 6), Utc(12, 31));
        Penalty(db, bookId, it, 6, Utc(9, 20), Utc(12, 31));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static DateTime Utc(int month, int day) => new(2026, month, day, 9, 0, 0, DateTimeKind.Utc);

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

    private static PenaltyRecord Penalty(
        LibraryDbContext db, Guid bookId, StaffMember staff, int points, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        var copy = new BookCopy(Guid.NewGuid(), bookId, $"PR-{Guid.NewGuid():N}"[..20], BookCopyCondition.Good, null);
        db.BookCopies.Add(copy);
        var loan = Loan.Issue(Guid.NewGuid(), copy.Id, staff.Id, createdAtUtc.AddDays(-30), 21);
        db.Loans.Add(loan);

        var penalty = PenaltyRecord.Create(
            Guid.NewGuid(), staff.Id, loan.Id, points, "Returned late", createdAtUtc, expiresAtUtc);
        db.PenaltyRecords.Add(penalty);
        return penalty;
    }
}

public sealed class PenaltyReportRepositoryTests(PenaltyReportDatabase database)
    : IClassFixture<PenaltyReportDatabase>
{
    private static readonly DateTime OctoberStartUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OctoberEndUtc = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    [Fact]
    public async Task Sums_penalty_points_in_the_period_per_department()
    {
        await using var db = database.NewContext();
        var repository = new ReportingReadRepository(db, Options.Create(new ReportingOptions
        {
            DashboardCacheMinutes = 5,
            TopBooksLimit = 10,
            TopCategoriesLimit = 5
        }));

        var rows = await repository.GetDepartmentPenaltiesAsync(OctoberStartUtc, OctoberEndUtc, PenaltyReportDatabase.NowUtc);

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, row => row.DepartmentId == PenaltyReportDatabase.LegalId);
        Assert.Equal(new DepartmentPenaltiesData(PenaltyReportDatabase.FinanceId, 4, 10, 2, 3),
            Assert.Single(rows, row => row.DepartmentId == PenaltyReportDatabase.FinanceId));
        Assert.Equal(new DepartmentPenaltiesData(PenaltyReportDatabase.ItId, 1, 5, 0, 5),
            Assert.Single(rows, row => row.DepartmentId == PenaltyReportDatabase.ItId));
    }
}
