using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class StaffStatusChangedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    private static StaffStatusChanged Event(
        StaffMemberStatus oldStatus, StaffMemberStatus newStatus, DateTime? effectiveUntilUtc = null) =>
        new(Guid.NewGuid(), Now, StaffId, oldStatus, newStatus, "Penalty threshold reached", effectiveUntilUtc);

    private static (StaffStatusChangedEventHandler Handler, RecordingCreator Creator) Create()
    {
        var creator = new RecordingCreator();
        return (new StaffStatusChangedEventHandler(creator, new FixedYerevanClock(Now)), creator);
    }

    [Fact]
    public async Task Restricted_creates_an_outlook_only_notice_without_cc()
    {
        var (handler, creator) = Create();
        var domainEvent = Event(StaffMemberStatus.Active, StaffMemberStatus.Restricted);

        await handler.HandleAsync(domainEvent, CancellationToken.None);

        var call = Assert.Single(creator.Calls);
        Assert.Equal(NotificationType.AccessRestricted, call.Type);
        Assert.Equal(StaffId, call.StaffMemberId);
        Assert.Equal(domainEvent.EventId, call.EventId);
        Assert.Equal([NotificationChannel.Outlook], call.Channels);
        Assert.Equal("Penalty threshold reached", call.Values[NotificationPlaceholders.Reason]);
        Assert.False(call.CcLineManager);
        Assert.Null(call.LoanId);
    }

    [Theory]
    [InlineData(StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Restricted)]
    public async Task Suspended_creates_an_outlook_only_notice_with_the_line_manager_in_cc_and_the_yerevan_end_date(StaffMemberStatus from)
    {
        var (handler, creator) = Create();
        // 21:00 UTC on 12 Oct is 01:00 on 13 Oct in Yerevan.
        var until = new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc);

        await handler.HandleAsync(Event(from, StaffMemberStatus.Suspended, until), CancellationToken.None);

        var call = Assert.Single(creator.Calls);
        Assert.Equal(NotificationType.AccessSuspended, call.Type);
        Assert.Equal([NotificationChannel.Outlook], call.Channels);
        Assert.Equal("13.10.2026", call.Values[NotificationPlaceholders.EffectiveUntil]);
        Assert.Equal("Penalty threshold reached", call.Values[NotificationPlaceholders.Reason]);
        Assert.True(call.CcLineManager);
    }

    [Fact]
    public async Task Suspended_without_an_end_date_is_reported_and_nothing_is_created()
    {
        var (handler, creator) = Create();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(Event(StaffMemberStatus.Active, StaffMemberStatus.Suspended), CancellationToken.None));

        Assert.Equal(NotificationErrors.EffectiveUntilRequired, ex.Error);
        Assert.Empty(creator.Calls);
    }

    [Theory]
    [InlineData(StaffMemberStatus.Suspended, StaffMemberStatus.Restricted)]   // suspension ended (daily recalculation)
    [InlineData(StaffMemberStatus.Restricted, StaffMemberStatus.Active)]      // points expired (daily recalculation)
    [InlineData(StaffMemberStatus.Suspended, StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Active, StaffMemberStatus.Deactivated)]
    [InlineData(StaffMemberStatus.Restricted, StaffMemberStatus.Deactivated)]
    [InlineData(StaffMemberStatus.Suspended, StaffMemberStatus.Deactivated)]
    [InlineData(StaffMemberStatus.Deactivated, StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Restricted, StaffMemberStatus.Restricted)]
    public async Task A_change_that_is_not_for_the_worse_creates_nothing(StaffMemberStatus from, StaffMemberStatus to)
    {
        var (handler, creator) = Create();

        await handler.HandleAsync(Event(from, to), CancellationToken.None);

        Assert.Empty(creator.Calls);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_handler_before_any_work()
    {
        var (handler, creator) = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleAsync(Event(StaffMemberStatus.Active, StaffMemberStatus.Restricted), cancellation.Token));

        Assert.Empty(creator.Calls);
    }

    private sealed record CreatorCall(
        NotificationType Type,
        Guid StaffMemberId,
        Guid EventId,
        IReadOnlyCollection<NotificationChannel> Channels,
        IReadOnlyDictionary<string, string> Values,
        Guid? LoanId,
        bool CcLineManager);

    private sealed class RecordingCreator : INotificationCreator
    {
        public List<CreatorCall> Calls { get; } = [];

        public Task CreateAsync(NotificationType type, Guid staffMemberId, Guid eventId,
            IReadOnlyCollection<NotificationChannel> channels, IReadOnlyDictionary<string, string> values,
            Guid? loanId = null, bool ccLineManager = false, string? calendarItem = null, CancellationToken ct = default)
        {
            Calls.Add(new CreatorCall(type, staffMemberId, eventId, channels, values, loanId, ccLineManager));
            return Task.CompletedTask;
        }
    }
}
