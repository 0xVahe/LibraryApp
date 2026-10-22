using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>Each test gets its own database: claiming changes the rows.</summary>
public sealed class NotificationDeliveryStoreTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LeaseUntil = Now.AddMinutes(15);
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly string _connectionString = TestDatabase.NewDatabase();
    private readonly Guid _older = Guid.NewGuid();
    private readonly Guid _newer = Guid.NewGuid();

    [Fact]
    public async Task Claims_pending_outlook_only_oldest_first()
    {
        await using var db = NewContext();

        var claimed = await new NotificationDeliveryStore(db).ClaimDueAsync(Now, LeaseUntil, 20);

        Assert.Equal(new[] { _older, _newer }, claimed.Select(n => n.Id));
        Assert.All(claimed, n => Assert.Equal(LeaseUntil, n.NextAttemptAtUtc));
    }

    [Fact]
    public async Task Claims_a_failed_email_when_its_retry_is_due()
    {
        var due = New(Guid.NewGuid(), NotificationChannel.Outlook, Now.AddHours(-4));
        due.MarkFailed("Mail server is down.", Now);
        var later = New(Guid.NewGuid(), NotificationChannel.Outlook, Now.AddHours(-4));
        later.MarkFailed("Mail server is down.", Now.AddMinutes(1));
        await using (var setup = NewContext())
        {
            setup.Notifications.AddRange(due, later);
            await setup.SaveChangesAsync();
        }

        await using var db = NewContext();
        var claimed = await new NotificationDeliveryStore(db).ClaimDueAsync(Now, LeaseUntil, 20);

        // The Failed email without a retry time (seeded) and the one due later are not claimed.
        Assert.Equal(new[] { due.Id, _older, _newer }, claimed.Select(n => n.Id));
        Assert.Equal(NotificationStatus.Failed, claimed[0].Status);
        Assert.Equal(LeaseUntil, claimed[0].NextAttemptAtUtc);
    }

    [Fact]
    public async Task Claims_at_most_max()
    {
        await using var db = NewContext();

        var claimed = await new NotificationDeliveryStore(db).ClaimDueAsync(Now, LeaseUntil, 1);

        Assert.Equal(_older, Assert.Single(claimed).Id);
    }

    [Fact]
    public async Task Claimed_email_is_not_claimed_again_until_the_lease_ends()
    {
        await using (var first = NewContext())
            await new NotificationDeliveryStore(first).ClaimDueAsync(Now, LeaseUntil, 20);

        await using var db = NewContext();
        var store = new NotificationDeliveryStore(db);

        Assert.Empty(await store.ClaimDueAsync(Now.AddMinutes(1), Now.AddMinutes(16), 20));
        Assert.Equal(2, (await store.ClaimDueAsync(LeaseUntil, LeaseUntil.AddMinutes(15), 20)).Count);
    }

    [Fact]
    public async Task Two_instances_never_claim_the_same_email()
    {
        await using var first = NewContext();
        await using var second = NewContext();

        // The first claim stays uncommitted, as if that instance were still in the middle of it. The retrying
        // execution strategy allows an own transaction only inside ExecuteAsync.
        IReadOnlyList<Notification> firstClaim = [];
        IReadOnlyList<Notification> secondClaim = [];
        await first.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await first.Database.BeginTransactionAsync();
            firstClaim = await new NotificationDeliveryStore(first).ClaimDueAsync(Now, LeaseUntil, 1);
            secondClaim = await new NotificationDeliveryStore(second).ClaimDueAsync(Now, LeaseUntil, 20);
            await transaction.CommitAsync();
        });

        // Rows the first instance is still reading are skipped, not waited for: the second instance may get fewer
        // rows, never the same one. Whatever it skipped is claimed by the next round.
        await using var third = NewContext();
        var nextRound = await new NotificationDeliveryStore(third).ClaimDueAsync(Now, LeaseUntil, 20);

        Assert.Equal(_older, Assert.Single(firstClaim).Id);
        Assert.DoesNotContain(secondClaim, n => n.Id == _older);
        Assert.Equal(new[] { _newer }, secondClaim.Concat(nextRound).Select(n => n.Id));
    }

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var departmentId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        db.Departments.Add(new Department(departmentId, "Engineering"));
        db.StaffCategories.Add(new StaffCategory(categoryId, "Employee", 3, 21, 1));
        db.Users.Add(new AppUser
        {
            Id = StaffId,
            UserName = StaffId.ToString("N"),
            NormalizedUserName = StaffId.ToString("N").ToUpperInvariant(),
            Email = $"{StaffId:N}@example.com",
            NormalizedEmail = $"{StaffId:N}@EXAMPLE.COM",
        });
        db.StaffMembers.Add(new StaffMember(
            StaffId, StaffId, "ENG-001", "Test", "Employee", "eng-001@example.com",
            departmentId, "Developer", null, categoryId, Now.AddYears(-1)));

        var sent = New(Guid.NewGuid(), NotificationChannel.Outlook, Now.AddHours(-3));
        sent.MarkSent(Now);
        var failed = New(Guid.NewGuid(), NotificationChannel.Outlook, Now.AddHours(-3));
        failed.MarkFailed("Mail server is down.", null);

        db.Notifications.AddRange(
            New(_newer, NotificationChannel.Outlook, Now.AddHours(-1)),
            New(_older, NotificationChannel.Outlook, Now.AddHours(-2)),
            New(Guid.NewGuid(), NotificationChannel.InApp, Now.AddHours(-3)),
            sent,
            failed);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    private static Notification New(Guid id, NotificationChannel channel, DateTime createdAtUtc) =>
        Notification.Create(
            id, StaffId, NotificationType.LoanConfirmation, channel, "eng-001@example.com", null,
            "Subject", "<p>Body</p>", createdAtUtc, DateOnly.FromDateTime(createdAtUtc));
}
