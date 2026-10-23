using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class PenaltyAppliedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    [Fact]
    public async Task Passes_points_total_reason_and_the_loan_to_the_creator_for_both_channels()
    {
        var creator = new RecordingCreator();
        var domainEvent = new PenaltyApplied(Guid.NewGuid(), Now, StaffId, Guid.NewGuid(), 4, 7, "Returned 4 working days late");

        await new PenaltyAppliedEventHandler(creator).HandleAsync(domainEvent, CancellationToken.None);

        var call = Assert.Single(creator.Calls);
        Assert.Equal(NotificationType.PenaltyApplied, call.Type);
        Assert.Equal(StaffId, call.StaffMemberId);
        Assert.Equal(domainEvent.EventId, call.EventId);
        Assert.Equal(domainEvent.LoanId, call.LoanId);
        Assert.Equal([NotificationChannel.Outlook, NotificationChannel.InApp], call.Channels);
        Assert.Equal("4", call.Values[NotificationPlaceholders.Points]);
        Assert.Equal("7", call.Values[NotificationPlaceholders.TotalPoints]);
        Assert.Equal("Returned 4 working days late", call.Values[NotificationPlaceholders.Reason]);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_handler_before_any_work()
    {
        var creator = new RecordingCreator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var domainEvent = new PenaltyApplied(Guid.NewGuid(), Now, StaffId, Guid.NewGuid(), 1, 1, "Late");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PenaltyAppliedEventHandler(creator).HandleAsync(domainEvent, cancellation.Token));

        Assert.Empty(creator.Calls);
    }

    private sealed record CreatorCall(
        NotificationType Type,
        Guid StaffMemberId,
        Guid EventId,
        IReadOnlyCollection<NotificationChannel> Channels,
        IReadOnlyDictionary<string, string> Values,
        Guid? LoanId);

    private sealed class RecordingCreator : INotificationCreator
    {
        public List<CreatorCall> Calls { get; } = [];

        public Task CreateAsync(NotificationType type, Guid staffMemberId, Guid eventId,
            IReadOnlyCollection<NotificationChannel> channels, IReadOnlyDictionary<string, string> values,
            Guid? loanId = null, bool ccLineManager = false, string? calendarItem = null, CancellationToken ct = default)
        {
            Calls.Add(new CreatorCall(type, staffMemberId, eventId, channels, values, loanId));
            return Task.CompletedTask;
        }
    }
}
