using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class StaffPasswordChangedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    [Fact]
    public async Task Creates_the_password_changed_notice_for_the_staff_member_on_both_channels_without_a_loan()
    {
        var creator = new RecordingCreator();
        var domainEvent = new StaffPasswordChanged(Guid.NewGuid(), Now, StaffId);

        await new StaffPasswordChangedEventHandler(creator).HandleAsync(domainEvent, CancellationToken.None);

        var call = Assert.Single(creator.Calls);
        Assert.Equal(NotificationType.PasswordChanged, call.Type);
        Assert.Equal(StaffId, call.StaffMemberId);
        Assert.Equal(domainEvent.EventId, call.EventId);
        Assert.Equal([NotificationChannel.Outlook, NotificationChannel.InApp], call.Channels);
        Assert.Empty(call.Values);   // the template only needs {{EmployeeName}}, added by the creator
        Assert.Null(call.LoanId);
        Assert.False(call.CcLineManager);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_handler_before_any_work()
    {
        var creator = new RecordingCreator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new StaffPasswordChangedEventHandler(creator)
                .HandleAsync(new StaffPasswordChanged(Guid.NewGuid(), Now, StaffId), cancellation.Token));

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
