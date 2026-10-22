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

public sealed class LoanRenewedEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();

    [Fact]
    public async Task Passes_the_book_title_the_yerevan_due_date_and_the_loan_to_the_creator_for_the_outlook_channel()
    {
        var fixture = new Fixture();
        // 21:00 UTC on 12 Oct is 01:00 on 13 Oct in Yerevan: the email shows the local date.
        var domainEvent = fixture.Event(newDueAtUtc: new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc));

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        var call = Assert.Single(fixture.Creator.Calls);
        Assert.Equal(NotificationType.DueDateCalendarItem, call.Type);
        Assert.Equal(StaffId, call.StaffMemberId);
        Assert.Equal(domainEvent.EventId, call.EventId);
        Assert.Equal(domainEvent.LoanId, call.LoanId);
        Assert.Equal([NotificationChannel.Outlook], call.Channels);
        Assert.Equal("Clean Code", call.Values[NotificationPlaceholders.BookTitle]);
        Assert.Equal("13.10.2026", call.Values[NotificationPlaceholders.DueDate]);
    }

    [Fact]
    public async Task The_email_carries_the_updated_calendar_item_with_the_same_uid_and_the_renewal_count_as_sequence()
    {
        var fixture = new Fixture();
        var domainEvent = fixture.Event(newDueAtUtc: new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc));

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        var ics = Assert.Single(fixture.Creator.Calls).CalendarItem!.Split("\r\n");
        Assert.Contains($"UID:loan-{domainEvent.LoanId:D}@library.ameriabank", ics);   // the UID of the issue: Outlook moves the event
        Assert.Contains("DTSTART;VALUE=DATE:20261013", ics);                           // the new due date, Yerevan day
        Assert.Contains($"SEQUENCE:{domainEvent.RenewalCount}", ics);
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

        public LoanRenewedEventHandler Handler { get; }

        public Fixture() =>
            Handler = new LoanRenewedEventHandler(Creator, new FakeCatalog(), new FixedYerevanClock(Now));

        public LoanRenewed Event(Guid? bookId = null, DateTime? newDueAtUtc = null) =>
            new(Guid.NewGuid(), Now, Guid.NewGuid(), StaffId, bookId ?? BookId, newDueAtUtc ?? Now.AddDays(21), 1);
    }

    public sealed record CreatorCall(
        NotificationType Type,
        Guid StaffMemberId,
        Guid EventId,
        IReadOnlyCollection<NotificationChannel> Channels,
        IReadOnlyDictionary<string, string> Values,
        Guid? LoanId,
        string? CalendarItem);

    private sealed class RecordingCreator : INotificationCreator
    {
        public List<CreatorCall> Calls { get; } = [];

        public Task CreateAsync(NotificationType type, Guid staffMemberId, Guid eventId,
            IReadOnlyCollection<NotificationChannel> channels, IReadOnlyDictionary<string, string> values,
            Guid? loanId = null, bool ccLineManager = false, string? calendarItem = null, CancellationToken ct = default)
        {
            Calls.Add(new CreatorCall(type, staffMemberId, eventId, channels, values, loanId, calendarItem));
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
