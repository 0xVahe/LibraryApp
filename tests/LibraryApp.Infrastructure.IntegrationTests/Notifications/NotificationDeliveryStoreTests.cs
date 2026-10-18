using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

public sealed class DeliveryDatabase : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    public Guid Older { get; } = Guid.NewGuid();
    public Guid Newer { get; } = Guid.NewGuid();

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
            New(Newer, NotificationChannel.Outlook, Now.AddHours(-1)),
            New(Older, NotificationChannel.Outlook, Now.AddHours(-2)),
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

    private static Notification New(Guid id, NotificationChannel channel, DateTime createdAtUtc) =>
        Notification.Create(
            id, StaffId, NotificationType.LoanConfirmation, channel, "eng-001@example.com", null,
            "Subject", "<p>Body</p>", createdAtUtc, DateOnly.FromDateTime(createdAtUtc));
}

public sealed class NotificationDeliveryStoreTests(DeliveryDatabase database) : IClassFixture<DeliveryDatabase>
{
    [Fact]
    public async Task Returns_pending_outlook_only_oldest_first()
    {
        await using var db = database.NewContext();

        var pending = await new NotificationDeliveryStore(db).GetPendingAsync(20);

        Assert.Equal(new[] { database.Older, database.Newer }, pending.Select(n => n.Id));
    }

    [Fact]
    public async Task Returns_at_most_max()
    {
        await using var db = database.NewContext();

        var pending = await new NotificationDeliveryStore(db).GetPendingAsync(1);

        Assert.Equal(database.Older, Assert.Single(pending).Id);
    }
}
