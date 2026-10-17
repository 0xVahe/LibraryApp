using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class NotificationServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Me = Guid.NewGuid();

    [Fact]
    public async Task MarkReadAsync_marks_own_notification_and_keeps_the_first_time_when_repeated()
    {
        var fixture = new Fixture();
        var notification = fixture.Add(Me);

        var first = await fixture.Service.MarkReadAsync(notification.Id);
        fixture.Clock.UtcNow = Now.AddHours(1);
        var second = await fixture.Service.MarkReadAsync(notification.Id);

        Assert.True(first.IsRead);
        Assert.Equal(Now, first.ReadAtUtc);
        Assert.Equal(Now, second.ReadAtUtc);
        Assert.Equal(Now, notification.ReadAtUtc);
        Assert.Equal(2, fixture.Repository.Commits);
    }

    [Fact]
    public async Task MarkReadAsync_reports_another_persons_notification_as_not_found_and_leaves_it_unread()
    {
        var fixture = new Fixture();
        var other = fixture.Add(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<DomainException>(() => fixture.Service.MarkReadAsync(other.Id));

        Assert.Equal(NotificationErrors.NotificationNotFound, ex.Error);
        Assert.Null(other.ReadAtUtc);
    }

    [Fact]
    public async Task MarkReadAsync_reports_an_unknown_id_and_a_caller_without_staff_profile_as_not_found()
    {
        var fixture = new Fixture();
        var own = fixture.Add(Me);

        var unknown = await Assert.ThrowsAsync<DomainException>(() => fixture.Service.MarkReadAsync(Guid.NewGuid()));
        fixture.User.StaffMemberId = null;
        var noProfile = await Assert.ThrowsAsync<DomainException>(() => fixture.Service.MarkReadAsync(own.Id));

        Assert.Equal(NotificationErrors.NotificationNotFound, unknown.Error);
        Assert.Equal(NotificationErrors.NotificationNotFound, noProfile.Error);
    }

    [Fact]
    public async Task ListAsync_asks_only_for_the_callers_own_notifications()
    {
        var fixture = new Fixture();
        fixture.Add(Me);
        fixture.Add(Guid.NewGuid());

        var page = await fixture.Service.ListAsync(unreadOnly: true, new PageRequest(2, 5));

        Assert.Equal(Me, fixture.Repository.LastStaffMemberId);
        Assert.True(fixture.Repository.LastUnreadOnly);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(5, page.PageSize);
    }

    [Fact]
    public async Task ListAsync_returns_an_empty_page_for_a_caller_without_staff_profile()
    {
        var fixture = new Fixture();
        fixture.Add(Me);
        fixture.User.StaffMemberId = null;

        var page = await fixture.Service.ListAsync(unreadOnly: false, new PageRequest());

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Null(fixture.Repository.LastStaffMemberId);
    }

    private sealed class Fixture
    {
        public FakeCurrentUser User { get; } = new() { StaffMemberId = Me, Role = StaffRoles.StaffMember };
        public TestClock Clock { get; } = new(Now);
        public FakeRepository Repository { get; } = new();
        public NotificationService Service { get; }

        public Fixture() => Service = new NotificationService(Repository, Repository, User, Clock);

        public Notification Add(Guid staffMemberId)
        {
            var notification = Notification.Create(
                Guid.NewGuid(), staffMemberId, NotificationType.LoanConfirmation, NotificationChannel.InApp,
                "a@ameriabank.am", null, "Subject", "<p>Body</p>", Now.AddMinutes(-5), DateOnly.FromDateTime(Now));
            Repository.Items.Add(notification);
            return notification;
        }
    }

    private sealed class FakeRepository : INotificationRepository, IUnitOfWork
    {
        public List<Notification> Items { get; } = [];
        public Guid? LastStaffMemberId { get; private set; }
        public bool LastUnreadOnly { get; private set; }
        public int Commits { get; private set; }

        public Task<PagedResult<Notification>> SearchInAppAsync(
            Guid staffMemberId, bool unreadOnly, PageRequest paging, CancellationToken ct = default)
        {
            LastStaffMemberId = staffMemberId;
            LastUnreadOnly = unreadOnly;
            var own = Items.Where(n => n.StaffMemberId == staffMemberId).ToList();
            return Task.FromResult(new PagedResult<Notification>(own, own.Count, paging.Page, paging.PageSize));
        }

        public Task<Notification?> FindOwnedAsync(Guid notificationId, Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(n => n.Id == notificationId && n.StaffMemberId == staffMemberId));

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            T result = await operation(ct);
            Commits++;
            return result;
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            ExecuteInTransactionAsync<object?>(async token =>
            {
                await operation(token);
                return null;
            }, ct);
    }
}
