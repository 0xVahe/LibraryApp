using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Ports;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class StaffEnrolledEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 21, 30, 0, DateTimeKind.Utc);   // 01:30 on 10 Oct in Yerevan
    private static readonly Guid StaffId = Guid.NewGuid();

    [Fact]
    public async Task Creates_a_pending_email_and_a_delivered_in_app_welcome_with_the_rendered_template()
    {
        var fixture = new Fixture();
        var domainEvent = fixture.Event();

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        Assert.Equal(2, fixture.Notifications.Items.Count);
        Assert.All(fixture.Notifications.Items, n =>
        {
            Assert.Equal(NotificationType.Welcome, n.Type);
            Assert.Equal(StaffId, n.StaffMemberId);
            Assert.Equal(domainEvent.EventId, n.EventId);
            Assert.Equal("anna@ameriabank.am", n.RecipientEmail);
            Assert.Null(n.CcEmail);
            Assert.Null(n.LoanId);
            Assert.Equal("Welcome Anna Petrosyan", n.Subject);
            Assert.Contains("Dear Anna Petrosyan,", n.Body);
            Assert.Equal(Now, n.CreatedAtUtc);
            Assert.Equal(new DateOnly(2026, 10, 10), n.LocalDate);   // Yerevan date, not the UTC date
        });

        var email = fixture.Notifications.Items.Single(n => n.Channel == NotificationChannel.Outlook);
        Assert.Equal(NotificationStatus.Pending, email.Status);   // the sender sends it later (NOT-08)
        Assert.Null(email.SentAtUtc);

        var inApp = fixture.Notifications.Items.Single(n => n.Channel == NotificationChannel.InApp);
        Assert.Equal(NotificationStatus.Sent, inApp.Status);
        Assert.Null(inApp.ReadAtUtc);
    }

    [Theory]
    [InlineData(NotificationChannel.Outlook, NotificationStatus.Pending)]
    [InlineData(NotificationChannel.InApp, NotificationStatus.Sent)]
    public async Task The_creator_stores_only_the_requested_channel(NotificationChannel channel, NotificationStatus status)
    {
        var fixture = new Fixture();

        await fixture.Creator.CreateAsync(
            NotificationType.Welcome, StaffId, Guid.NewGuid(), [channel], new Dictionary<string, string>());

        var row = Assert.Single(fixture.Notifications.Items);
        Assert.Equal(channel, row.Channel);
        Assert.Equal(status, row.Status);
    }

    [Fact]
    public async Task The_creator_ignores_a_repeated_channel_and_refuses_no_channel_or_an_undefined_one()
    {
        var fixture = new Fixture();
        var none = new Dictionary<string, string>();

        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(),
            [NotificationChannel.Outlook, NotificationChannel.Outlook], none);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(), [], none));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(), [(NotificationChannel)99], none));

        Assert.Single(fixture.Notifications.Items);
    }

    [Fact]
    public async Task Handling_the_same_event_twice_creates_nothing_new()
    {
        var fixture = new Fixture();
        var domainEvent = fixture.Event();

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);
        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        Assert.Equal(2, fixture.Notifications.Items.Count);
    }

    [Fact]
    public async Task Only_the_missing_channel_is_created_when_the_event_already_has_the_other_one()
    {
        var fixture = new Fixture();
        var domainEvent = fixture.Event();
        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, domainEvent.EventId,
            [NotificationChannel.Outlook], new Dictionary<string, string>());

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        Assert.Equal(2, fixture.Notifications.Items.Count);
        Assert.Single(fixture.Notifications.Items, n => n.Channel == NotificationChannel.Outlook);
        Assert.Single(fixture.Notifications.Items, n => n.Channel == NotificationChannel.InApp);
    }

    [Fact]
    public async Task A_loan_notification_stores_the_loan_and_a_second_event_for_it_on_the_same_day_is_skipped()
    {
        var fixture = new Fixture();
        var loanId = Guid.NewGuid();
        var none = new Dictionary<string, string>();

        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(), [NotificationChannel.InApp], none, loanId);
        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(), [NotificationChannel.InApp], none, loanId);
        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(), [NotificationChannel.InApp], none, Guid.NewGuid());

        Assert.Equal(2, fixture.Notifications.Items.Count);   // the second call for the same loan and day is ignored (NOT-06)
        Assert.Equal(loanId, fixture.Notifications.Items[0].LoanId);
    }

    [Fact]
    public async Task The_line_manager_is_copied_only_on_the_outlook_row_and_only_when_asked()
    {
        var fixture = new Fixture();
        var none = new Dictionary<string, string>();
        var both = new[] { NotificationChannel.Outlook, NotificationChannel.InApp };
        var copied = Guid.NewGuid();
        var notCopied = Guid.NewGuid();

        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, copied, both, none, ccLineManager: true);
        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, notCopied, both, none);

        Assert.Equal("manager@ameriabank.am",
            fixture.Notifications.Items.Single(n => n.EventId == copied && n.Channel == NotificationChannel.Outlook).CcEmail);
        Assert.Null(fixture.Notifications.Items.Single(n => n.EventId == copied && n.Channel == NotificationChannel.InApp).CcEmail);
        Assert.All(fixture.Notifications.Items.Where(n => n.EventId == notCopied), n => Assert.Null(n.CcEmail));
    }

    [Fact]
    public async Task The_calendar_item_is_stored_on_the_outlook_row_only()
    {
        var fixture = new Fixture();
        var both = new[] { NotificationChannel.Outlook, NotificationChannel.InApp };

        await fixture.Creator.CreateAsync(NotificationType.Welcome, StaffId, Guid.NewGuid(), both,
            new Dictionary<string, string>(), calendarItem: "BEGIN:VCALENDAR");

        Assert.Equal("BEGIN:VCALENDAR", fixture.Notifications.Items.Single(n => n.Channel == NotificationChannel.Outlook).CalendarItem);
        Assert.Null(fixture.Notifications.Items.Single(n => n.Channel == NotificationChannel.InApp).CalendarItem);
    }

    [Fact]
    public async Task A_different_event_for_the_same_member_creates_its_own_notifications()
    {
        var fixture = new Fixture();

        await fixture.Handler.HandleAsync(fixture.Event(), CancellationToken.None);
        await fixture.Handler.HandleAsync(fixture.Event(), CancellationToken.None);

        Assert.Equal(4, fixture.Notifications.Items.Count);
    }

    [Fact]
    public async Task Unknown_staff_member_is_reported_and_nothing_is_stored()
    {
        var fixture = new Fixture();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            fixture.Handler.HandleAsync(fixture.Event(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(NotificationErrors.RecipientNotFound, ex.Error);
        Assert.Empty(fixture.Notifications.Items);
    }

    [Fact]
    public async Task Missing_active_template_is_reported_and_nothing_is_stored()
    {
        var fixture = new Fixture { Template = null };

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            fixture.Handler.HandleAsync(fixture.Event(), CancellationToken.None));

        Assert.Equal(NotificationErrors.TemplateNotFound, ex.Error);
        Assert.Empty(fixture.Notifications.Items);
    }

    [Fact]
    public async Task A_template_with_an_unknown_placeholder_fails_and_nothing_is_stored()
    {
        var fixture = new Fixture { Template = new NotificationTemplate(Guid.NewGuid(), NotificationType.Welcome, "Hi", "<p>{{Nope}}</p>") };

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Handler.HandleAsync(fixture.Event(), CancellationToken.None));

        Assert.Empty(fixture.Notifications.Items);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_handler_before_any_work()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Handler.HandleAsync(fixture.Event(), cancellation.Token));

        Assert.Empty(fixture.Notifications.Items);
    }

    private sealed class Fixture
    {
        public FakeNotifications Notifications { get; } = new();

        public NotificationTemplate? Template { get; set; } = new(
            Guid.NewGuid(), NotificationType.Welcome, "Welcome {{EmployeeName}}", "<p>Dear {{EmployeeName}},</p>");

        public NotificationCreator Creator { get; }

        public StaffEnrolledEventHandler Handler { get; }

        public Fixture()
        {
            Creator = new NotificationCreator(
                Notifications,
                new FakeTemplates(this),
                new FakeRecipients(),
                new NotificationTemplateRenderer(),
                new CountingIds(),
                new FixedYerevanClock(Now));
            Handler = new StaffEnrolledEventHandler(Creator);
        }

        public StaffEnrolled Event(Guid? staffMemberId = null) => new(Guid.NewGuid(), Now, staffMemberId ?? StaffId);
    }

    private sealed class FakeNotifications : INotificationRepository
    {
        public List<Notification> Items { get; } = [];

        public Task AddAsync(Notification notification, CancellationToken ct = default)
        {
            Items.Add(notification);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsForEventAsync(Guid eventId, NotificationType type, NotificationChannel channel, CancellationToken ct = default) =>
            Task.FromResult(Items.Any(n => n.EventId == eventId && n.Type == type && n.Channel == channel));

        public Task<bool> ExistsForLoanAsync(Guid loanId, NotificationType type, NotificationChannel channel, DateOnly localDate, CancellationToken ct = default) =>
            Task.FromResult(Items.Any(n => n.LoanId == loanId && n.Type == type && n.Channel == channel && n.LocalDate == localDate));

        public Task<PagedResult<Notification>> SearchInAppAsync(Guid staffMemberId, bool unreadOnly, PageRequest paging, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Notification?> FindOwnedAsync(Guid notificationId, Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTemplates(Fixture fixture) : INotificationTemplateRepository
    {
        public Task<IReadOnlyList<NotificationTemplate>> ListAsync(NotificationType? type, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NotificationTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(NotificationTemplate template) => throw new NotSupportedException();
        public void Remove(NotificationTemplate template) => throw new NotSupportedException();
        public Task DeactivateOthersAsync(NotificationType type, Guid exceptId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<NotificationTemplate?> FindActiveAsync(NotificationType type, CancellationToken ct = default) =>
            Task.FromResult(type == NotificationType.Welcome ? fixture.Template : null);
    }

    private sealed class FakeRecipients : INotificationRecipientReader
    {
        public Task<NotificationRecipient?> FindAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult(staffMemberId == StaffId
                ? new NotificationRecipient(StaffId, "Anna Petrosyan", "anna@ameriabank.am", "manager@ameriabank.am")
                : null);
    }

    private sealed class CountingIds : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
