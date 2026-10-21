using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Notifications.EventHandlers;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Notifications;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class LoanIssuedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();

    [Fact]
    public async Task Creates_the_loan_confirmation_on_both_channels_and_the_calendar_item_on_outlook_with_the_book_and_yerevan_due_date()
    {
        var fixture = new Fixture();
        // 21:00 UTC on 12 Oct is 01:00 on 13 Oct in Yerevan: the email shows the local date.
        var domainEvent = fixture.Event(dueAtUtc: new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc));

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        Assert.Equal(2, fixture.Creator.Calls.Count);

        var confirmation = fixture.Creator.Calls.Single(c => c.Type == NotificationType.LoanConfirmation);
        Assert.Equal([NotificationChannel.Outlook, NotificationChannel.InApp], confirmation.Channels);

        var calendar = fixture.Creator.Calls.Single(c => c.Type == NotificationType.DueDateCalendarItem);
        Assert.Equal([NotificationChannel.Outlook], calendar.Channels);

        Assert.All(fixture.Creator.Calls, call =>
        {
            Assert.Equal(StaffId, call.StaffMemberId);
            Assert.Equal(domainEvent.EventId, call.EventId);   // one event, two types
            Assert.Equal(domainEvent.LoanId, call.LoanId);
            Assert.Equal("Clean Code", call.Values[NotificationPlaceholders.BookTitle]);
            Assert.Equal("13.10.2026", call.Values[NotificationPlaceholders.DueDate]);
        });
    }

    [Fact]
    public async Task Unknown_book_is_reported_and_nothing_is_created()
    {
        var fixture = new Fixture();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            fixture.Handler.HandleAsync(fixture.Event(bookId: Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(NotificationErrors.BookNotFound, ex.Error);
        Assert.Empty(fixture.Creator.Calls);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_handler_before_any_work()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Handler.HandleAsync(fixture.Event(), cancellation.Token));

        Assert.Empty(fixture.Creator.Calls);
    }

    private sealed class Fixture
    {
        public RecordingCreator Creator { get; } = new();

        public LoanIssuedEventHandler Handler { get; }

        public Fixture() =>
            Handler = new LoanIssuedEventHandler(Creator, new FakeCatalog(), new FixedYerevanClock(Now));

        public LoanIssued Event(Guid? bookId = null, DateTime? dueAtUtc = null) =>
            new(Guid.NewGuid(), Now, Guid.NewGuid(), StaffId, Guid.NewGuid(), bookId ?? BookId, dueAtUtc ?? Now.AddDays(21));
    }

    public sealed record CreatorCall(
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
            Guid? loanId = null, bool ccLineManager = false, CancellationToken ct = default)
        {
            Calls.Add(new CreatorCall(type, staffMemberId, eventId, channels, values, loanId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCatalog : ICatalogService
    {
        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult(bookId == BookId
                ? new BookDto(BookId, "Clean Code", "9780132350884", 2008, "English", null, Guid.NewGuid(), "Software",
                    null, null, ["Robert C. Martin"], 1, 1, false)
                : null);

        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
