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

public sealed class ReservationReadyEventHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();

    [Fact]
    public async Task Passes_the_book_title_and_the_yerevan_pickup_date_to_the_creator_for_both_channels()
    {
        var fixture = new Fixture();
        // 21:00 UTC on 12 Oct is 01:00 on 13 Oct in Yerevan: the email shows the local date.
        var domainEvent = fixture.Event(readyUntilUtc: new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc));

        await fixture.Handler.HandleAsync(domainEvent, CancellationToken.None);

        var call = Assert.Single(fixture.Creator.Calls);
        Assert.Equal(NotificationType.ReservationReady, call.Type);
        Assert.Equal(StaffId, call.StaffMemberId);
        Assert.Equal(domainEvent.EventId, call.EventId);
        Assert.Equal([NotificationChannel.Outlook, NotificationChannel.InApp], call.Channels);
        Assert.Equal("Clean Code", call.Values[NotificationPlaceholders.BookTitle]);
        Assert.Equal("13.10.2026", call.Values[NotificationPlaceholders.ReadyUntil]);
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

        public ReservationReadyEventHandler Handler { get; }

        public Fixture() =>
            Handler = new ReservationReadyEventHandler(Creator, new FakeCatalog(), new FixedYerevanClock(Now));

        public ReservationReady Event(Guid? bookId = null, DateTime? readyUntilUtc = null) =>
            new(Guid.NewGuid(), Now, Guid.NewGuid(), bookId ?? BookId, StaffId, readyUntilUtc ?? Now.AddDays(3));
    }

    public sealed record CreatorCall(
        NotificationType Type,
        Guid StaffMemberId,
        Guid EventId,
        IReadOnlyCollection<NotificationChannel> Channels,
        IReadOnlyDictionary<string, string> Values);

    private sealed class RecordingCreator : INotificationCreator
    {
        public List<CreatorCall> Calls { get; } = [];

        public Task CreateAsync(NotificationType type, Guid staffMemberId, Guid eventId,
            IReadOnlyCollection<NotificationChannel> channels, IReadOnlyDictionary<string, string> values,
            CancellationToken ct = default)
        {
            Calls.Add(new CreatorCall(type, staffMemberId, eventId, channels, values));
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
