using LibraryApp.Application.Reporting.Options;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence.Repositories.Reporting;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Reporting;

public sealed class ReportingLimitsTests : IClassFixture<ReportingDatabase>
{
    private readonly ReportingDatabase _database;

    public ReportingLimitsTests(ReportingDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task Respects_configured_result_limits()
    {
        await using var db = _database.NewContext();

        var categoryId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var copyId = Guid.NewGuid();

        db.Categories.Add(new Category(categoryId, "Science"));

        db.Books.Add(new Book(
            bookId, "Physics", "9780131103627", 2020, "en",
            null, categoryId, null));

        db.BookCopies.Add(new BookCopy(
            copyId, bookId, "REP-LIMIT-001",
            BookCopyCondition.Good, null));

        db.Loans.Add(Loan.Issue(
            Guid.NewGuid(),
            copyId,
            ReportingDatabase.EngineeringStaffId,
            new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            21));

        await db.SaveChangesAsync();

        var repository = new ReportingReadRepository(
            db,
            Options.Create(new ReportingOptions
            {
                DashboardCacheMinutes = 5,
                TopBooksLimit = 1,
                TopCategoriesLimit = 1
            }));

        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

        var books = await repository.GetTopBooksAsync(from, to);
        var categories = await repository.GetTopCategoriesAsync(from, to);

        var topBook = Assert.Single(books);
        Assert.Equal(ReportingDatabase.BookId, topBook.BookId);
        Assert.Equal(3, topBook.LoanCount);

        var topCategory = Assert.Single(categories);
        Assert.Equal("Technology", topCategory.Name);
        Assert.Equal(3, topCategory.LoanCount);
    }
}
