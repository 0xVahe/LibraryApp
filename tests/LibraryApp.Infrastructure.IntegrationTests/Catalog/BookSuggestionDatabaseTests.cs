using System.Data;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.IntegrationTests.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Catalog;

/// <summary>CAT-10 SQL checks use the EF model in a temporary database; production migrations remain Integration Owner work.</summary>
public sealed class BookSuggestionDatabase : IAsyncLifetime
{
    private readonly string _connectionString = TestDatabase.NewConnectionString();
    public Guid FirstStaffId { get; } = Guid.NewGuid();
    public Guid SecondStaffId { get; } = Guid.NewGuid();
    public Guid FirstReviewerId { get; } = Guid.NewGuid();
    public Guid SecondReviewerId { get; } = Guid.NewGuid();
    public static readonly DateTime Now = new(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);

    public LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        var department = new Department(Guid.NewGuid(), "CAT-10 test department");
        var category = new StaffCategory(Guid.NewGuid(), "CAT-10 test category", 3, 14, 1);
        db.AddRange(department, category);
        foreach (var id in new[] { FirstStaffId, SecondStaffId })
        {
            var userId = Guid.NewGuid();
            db.Users.Add(new AppUser { Id = userId });
            db.StaffMembers.Add(new StaffMember(id, userId, id.ToString("N"), id == FirstStaffId ? "First" : "Second", "Member",
                $"{id:N}@ameriabank.am", department.Id, "Test", null, category.Id, Now));
        }
        db.Users.AddRange(new AppUser { Id = FirstReviewerId }, new AppUser { Id = SecondReviewerId });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }
}

public class BookSuggestionDatabaseTests(BookSuggestionDatabase database) : IClassFixture<BookSuggestionDatabase>
{
    private static BookSuggestionService Service(LibraryDbContext db, Guid? staffId, Guid reviewerId,
        IApplicationLock? applicationLock = null, DateTime? now = null) => new(
        new BookSuggestionRepository(db), new UnitOfWork(db), applicationLock ?? new SqlServerApplicationLock(db),
        new Caller(staffId, reviewerId), new FixedClock(now ?? BookSuggestionDatabase.Now), new SequentialGuidGenerator(TimeProvider.System));

    [Fact]
    public async Task Creation_and_own_lists_persist_and_filter_in_SQL()
    {
        await using var db = database.NewContext();
        var firstService = Service(db, database.FirstStaffId, database.FirstReviewerId);
        var secondService = Service(db, database.SecondStaffId, database.SecondReviewerId);
        var first = await firstService.CreateAsync(new("First", "Author", null, "Reason"));
        var second = await secondService.CreateAsync(new("Second", "Author", "9780132350884", "Reason"));
        Assert.Equal("First Member", first.StaffMemberName);
        Assert.Equal("Second Member", second.StaffMemberName);
        db.ChangeTracker.Clear();
        var firstPage = await firstService.ListMineAsync(new PageRequest(1, 100));
        var secondPage = await secondService.ListMineAsync(new PageRequest(1, 100));
        Assert.Contains(firstPage.Items, x => x.Id == first.Id && x.Status == BookSuggestionStatus.Pending);
        Assert.Contains(secondPage.Items, x => x.Id == second.Id);
        Assert.All(firstPage.Items, x => Assert.Equal(database.FirstStaffId, x.StaffMemberId));
        Assert.All(secondPage.Items, x => Assert.Equal(database.SecondStaffId, x.StaffMemberId));
        Assert.All(firstPage.Items, x => Assert.Equal("First Member", x.StaffMemberName));
        Assert.All(secondPage.Items, x => Assert.Equal("Second Member", x.StaffMemberName));
        Assert.DoesNotContain(firstPage.Items, x => x.Id == second.Id);
        Assert.DoesNotContain(secondPage.Items, x => x.Id == first.Id);
        Assert.Equal(firstPage.TotalCount, firstPage.Items.Count);
        var saved = await db.Set<BookSuggestion>().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(BookSuggestionDatabase.Now, saved.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
        Assert.Null(saved.ReviewedByUserId);
        Assert.Null(saved.ReviewedAtUtc);
    }

    [Fact]
    public async Task SQL_status_filter_and_paging_are_stable_and_preserve_total_count()
    {
        await using var db = database.NewContext();
        var service = Service(db, database.FirstStaffId, database.FirstReviewerId);
        for (var i = 0; i < 3; i++)
        {
            var created = await service.CreateAsync(new("Page title", "Author", null, "Reason"));
            await service.RejectAsync(created.Id, new("Comment"));
        }
        var all = await service.ListAsync(new(BookSuggestionStatus.Rejected, 1, 100));
        var page = await service.ListAsync(new(BookSuggestionStatus.Rejected, 2, 1));
        Assert.True(all.TotalCount >= 3);
        Assert.All(all.Items, x => Assert.Equal(BookSuggestionStatus.Rejected, x.Status));
        Assert.Equal(all.TotalCount, page.TotalCount);
        Assert.Equal(all.Items[1], Assert.Single(page.Items));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Review_persists_metadata_and_approval_creates_no_books_or_copies(bool reject)
    {
        await using var db = database.NewContext();
        var service = Service(db, database.FirstStaffId, database.FirstReviewerId);
        var created = await service.CreateAsync(new("Title", "Author", null, "Reason"));
        var books = await db.Books.IgnoreQueryFilters().CountAsync();
        var copies = await db.BookCopies.CountAsync();
        var reviewAt = BookSuggestionDatabase.Now.AddHours(1);
        var reviewer = Service(db, null, database.FirstReviewerId, now: reviewAt);
        var result = reject ? await reviewer.RejectAsync(created.Id, new("  Comment  ")) : await reviewer.ApproveAsync(created.Id);
        Assert.Equal("First Member", result.StaffMemberName);
        await using var verify = database.NewContext();
        var saved = await verify.Set<BookSuggestion>().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(result.Status, saved.Status);
        Assert.Equal(database.FirstReviewerId, saved.ReviewedByUserId);
        Assert.Equal(reviewAt, saved.ReviewedAtUtc);
        Assert.Equal(DateTimeKind.Utc, saved.ReviewedAtUtc!.Value.Kind);
        Assert.Equal(reject ? "Comment" : null, saved.ReviewComment);
        Assert.Equal(books, await verify.Books.IgnoreQueryFilters().CountAsync());
        Assert.Equal(copies, await verify.BookCopies.CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Competing_reviews_on_separate_SQL_connections_allow_exactly_one_transition(bool firstReject, bool secondReject)
    {
        await using var setup = database.NewContext();
        var created = await Service(setup, database.FirstStaffId, database.FirstReviewerId)
            .CreateAsync(new("Contested", "Author", null, "Reason"));
        await using var firstDb = database.NewContext();
        await using var secondDb = database.NewContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstLock = new ObservedLock(new SqlServerApplicationLock(firstDb), hold: true);
        var secondLock = new ObservedLock(new SqlServerApplicationLock(secondDb), hold: false);
        var first = Service(firstDb, null, database.FirstReviewerId, firstLock);
        var second = Service(secondDb, null, database.SecondReviewerId, secondLock, BookSuggestionDatabase.Now.AddHours(1));

        var winner = Review(first, firstReject, "First comment");
        Task<BookSuggestionDto>? loser = null;
        try
        {
            await firstLock.Acquired.Task.WaitAsync(timeout.Token);
            loser = Review(second, secondReject, "Second comment");
            await secondLock.Attempted.Task.WaitAsync(timeout.Token);
            Assert.False(secondLock.Acquired.Task.IsCompleted);
            Assert.False(loser.IsCompleted);
        }
        finally
        {
            firstLock.Release.TrySetResult();
        }
        var result = await winner;
        var error = await Assert.ThrowsAsync<DomainException>(() => loser!);
        Assert.Equal(CatalogErrors.SuggestionAlreadyReviewed, error.Error);
        await using var verify = database.NewContext();
        var saved = await verify.Set<BookSuggestion>().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(firstReject ? BookSuggestionStatus.Rejected : BookSuggestionStatus.Approved, saved.Status);
        Assert.Equal(result.Status, saved.Status);
        Assert.Equal(database.FirstReviewerId, saved.ReviewedByUserId);
        Assert.Equal(BookSuggestionDatabase.Now, saved.ReviewedAtUtc);
        Assert.Equal(firstReject ? "First comment" : null, saved.ReviewComment);

        Task<BookSuggestionDto> Review(BookSuggestionService service, bool reject, string comment) => reject
            ? service.RejectAsync(created.Id, new(comment), timeout.Token)
            : service.ApproveAsync(created.Id, timeout.Token);
    }

    [Fact]
    public async Task SQL_check_refuses_an_approved_suggestion_without_a_reviewer()
    {
        await using var db = database.NewContext();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            InsertThroughSqlAsync(db, BookSuggestionStatus.Approved, reviewedByUserId: null, reviewComment: null));

        // 547 is also the number of a foreign key error: the constraint name proves it is the CHECK.
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_BookSuggestions_Review", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task SQL_check_refuses_a_rejected_suggestion_without_a_comment(string? reviewComment)
    {
        await using var db = database.NewContext();

        // A real reviewer: the row breaks only the comment rule, not CK_BookSuggestions_Review or a foreign key.
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            InsertThroughSqlAsync(db, BookSuggestionStatus.Rejected, database.FirstReviewerId, reviewComment));

        Assert.Equal(547, error.Number);
        Assert.Contains("CK_BookSuggestions_RejectComment", error.Message);
    }

    /// <summary>
    /// Inserts a reviewed suggestion with plain SQL, past the entity's own checks. The staff member is a real one, so
    /// the foreign key on StaffMemberId never fails.
    /// </summary>
    private Task<int> InsertThroughSqlAsync(
        LibraryDbContext db, BookSuggestionStatus status, Guid? reviewedByUserId, string? reviewComment)
    {
        const string sql =
            "INSERT INTO [BookSuggestions] ([Id], [StaffMemberId], [Title], [AuthorName], [Reason], [Status], [CreatedAtUtc], [ReviewedAtUtc], [ReviewedByUserId], [ReviewComment]) " +
            "VALUES (@id, @staffMemberId, N'SQL title', N'SQL author', N'SQL reason', @status, @createdAtUtc, @reviewedAtUtc, @reviewedByUserId, @reviewComment)";

        return db.Database.ExecuteSqlRawAsync(sql,
            new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = Guid.NewGuid() },
            new SqlParameter("@staffMemberId", SqlDbType.UniqueIdentifier) { Value = database.FirstStaffId },
            new SqlParameter("@status", SqlDbType.Int) { Value = (int)status },
            new SqlParameter("@createdAtUtc", SqlDbType.DateTime2) { Value = BookSuggestionDatabase.Now },
            new SqlParameter("@reviewedAtUtc", SqlDbType.DateTime2) { Value = BookSuggestionDatabase.Now.AddHours(1) },
            new SqlParameter("@reviewedByUserId", SqlDbType.UniqueIdentifier) { Value = (object?)reviewedByUserId ?? DBNull.Value },
            new SqlParameter("@reviewComment", SqlDbType.NVarChar, BookSuggestion.ReviewCommentMaxLength) { Value = (object?)reviewComment ?? DBNull.Value });
    }

    private sealed class ObservedLock(IApplicationLock inner, bool hold) : IApplicationLock
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AcquireAsync(string resource, CancellationToken ct = default)
        {
            var acquisition = inner.AcquireAsync(resource, ct);
            Attempted.TrySetResult();
            await acquisition;
            Acquired.TrySetResult();
            if (hold) await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class Caller(Guid? staffId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid? StaffMemberId => staffId;
        public string Email => "sql-tests@ameriabank.am";
        public string Role => StaffRoles.LibraryAdministrator;
        public bool IsInRole(string role) => role == Role;
    }
}
