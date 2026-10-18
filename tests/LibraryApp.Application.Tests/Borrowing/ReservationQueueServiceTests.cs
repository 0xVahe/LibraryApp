using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Events;

namespace LibraryApp.Application.Tests.Borrowing;

/// <summary>
/// The reservation queue when a copy becomes free outside a loan (BOR-07, BQ-G1-10). The service runs inside the
/// caller's unit of work, so the fakes record what it changed instead of committing.
/// </summary>
public sealed class ReservationQueueServiceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid CopyId = Guid.NewGuid();

    // Thursday 15 Oct 2026: three working days later is Tuesday 20 Oct (the weekend is skipped).
    private static readonly DateTime Now = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndOfTuesday = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);

    [Fact]
    public async Task Nobody_waiting_changes_nothing()
    {
        var fixture = new Fixture();

        await fixture.Service.OfferCopyAsync(CopyId, BookId);

        Assert.Equal([$"lock:{BorrowingLocks.Book(BookId)}", "read:waiting"], fixture.Lock.Log);
        Assert.Empty(fixture.Catalog.StatusChanges);
        Assert.Empty(fixture.Events.Pending);
    }

    [Fact]
    public async Task The_first_waiting_reservation_gets_the_copy_and_the_others_move_up()
    {
        var fixture = new Fixture();
        Reservation first = fixture.Wait(1);
        Reservation second = fixture.Wait(2);
        Reservation third = fixture.Wait(3);

        await fixture.Service.OfferCopyAsync(CopyId, BookId);

        Assert.Equal(ReservationStatus.ReadyForPickup, first.Status);
        Assert.Equal(CopyId, first.CopyId);
        Assert.Equal(EndOfTuesday, first.ReadyUntilUtc);
        Assert.Equal(0, first.QueuePosition);
        Assert.Equal((ReservationStatus.Waiting, 1), (second.Status, second.QueuePosition));
        Assert.Equal((ReservationStatus.Waiting, 2), (third.Status, third.QueuePosition));
        Assert.Equal([(CopyId, BookCopyStatus.Reserved)], fixture.Catalog.StatusChanges);
    }

    [Theory]
    // Thursday 15 Oct: 5 working days end on Thursday 22 Oct; with the (working) release day counted, 3 days end on
    // Monday 19 Oct and 1 day ends the same Thursday.
    [InlineData(5, false, 22)]
    [InlineData(3, true, 19)]
    [InlineData(1, true, 15)]
    public async Task The_pickup_deadline_follows_the_settings(int pickupWorkingDays, bool countsReleaseDay, int lastDay)
    {
        var fixture = new Fixture(settings =>
        {
            settings.Reservations.PickupWorkingDays = pickupWorkingDays;
            settings.Reservations.PickupCountsReleaseDay = countsReleaseDay;
        });
        Reservation first = fixture.Wait(1);

        await fixture.Service.OfferCopyAsync(CopyId, BookId);

        Assert.Equal(new DateTime(2026, 10, lastDay + 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1), first.ReadyUntilUtc);
    }

    [Fact]
    public async Task ReservationReady_is_published_for_the_first_reservation()
    {
        var fixture = new Fixture();
        Reservation first = fixture.Wait(1);
        fixture.Wait(2);

        await fixture.Service.OfferCopyAsync(CopyId, BookId);

        var ready = Assert.IsType<ReservationReady>(Assert.Single(fixture.Events.Pending));
        Assert.Equal(fixture.Ids.Id, ready.EventId);
        Assert.Equal(Now, ready.OccurredAtUtc);
        Assert.Equal(first.Id, ready.ReservationId);
        Assert.Equal(BookId, ready.BookId);
        Assert.Equal(first.StaffMemberId, ready.StaffMemberId);
        Assert.Equal(EndOfTuesday, ready.ReadyUntilUtc);
    }

    [Fact]
    public async Task The_queue_lock_is_taken_before_the_queue_is_read()
    {
        var fixture = new Fixture();
        fixture.Wait(1);

        await fixture.Service.OfferCopyAsync(CopyId, BookId);

        Assert.Equal([$"lock:{BorrowingLocks.Book(BookId)}", "read:waiting"], fixture.Lock.Log.Take(2));
    }

    [Fact]
    public async Task A_failing_copy_change_publishes_no_event()
    {
        var fixture = new Fixture();
        fixture.Wait(1);
        fixture.Catalog.Failure = new InvalidOperationException("Simulated Catalog failure.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.OfferCopyAsync(CopyId, BookId));

        // The caller's unit of work rolls back the reservation change; no event was handed to the bus.
        Assert.Empty(fixture.Events.Pending);
    }

    private sealed class Fixture
    {
        public readonly FakeApplicationLock Lock = new();
        public readonly CatalogFake Catalog = new();
        public readonly EventBusFake Events = new();
        public readonly FakeIdGenerator Ids = new();
        public readonly WaitingQueueFake Reservations;
        public ReservationQueueService Service { get; }

        public Fixture(Action<BorrowingOptions>? settings = null)
        {
            Reservations = new WaitingQueueFake(Lock.Log);
            Service = new ReservationQueueService(
                Reservations, Catalog, Lock, Events, new WeekdayCalendar(), new TestClock(Now), Ids,
                BorrowingTestSettings.Create(settings));
        }

        /// <summary>Adds a Waiting reservation of the book at this queue position, created before now.</summary>
        public Reservation Wait(int position)
        {
            var reservation = Reservation.Create(
                Guid.NewGuid(), BookId, Guid.NewGuid(), Now.AddDays(-10).AddMinutes(position), position);
            Reservations.Waiting.Add(reservation);
            return reservation;
        }
    }

    private sealed class WaitingQueueFake(List<string> log) : IReservationRepository
    {
        public List<Reservation> Waiting { get; } = new();

        public Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(Guid bookId, CancellationToken ct = default)
        {
            log.Add("read:waiting");
            return Task.FromResult<IReadOnlyList<Reservation>>(Waiting.Where(r => r.BookId == bookId).ToList());
        }

        public Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Reservation reservation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CatalogFake : ICatalogService
    {
        public List<(Guid CopyId, BookCopyStatus Status)> StatusChanges { get; } = new();
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default)
        {
            if (Failure is not null)
                throw Failure;
            StatusChanges.Add((copyId, status));
            return Task.CompletedTask;
        }

        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Keeps what was published; the caller's unit of work, not this service, decides whether it is saved.</summary>
    private sealed class EventBusFake : Abstractions.Messaging.IEventBus
    {
        public List<Domain.Common.IDomainEvent> Pending { get; } = new();

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
            where TEvent : class, Domain.Common.IDomainEvent
        {
            Pending.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>Monday to Friday, no holidays: enough to show that the deadline counts working days.</summary>
    private sealed class WeekdayCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public DateOnly AddWorkingDays(DateOnly from, int days)
        {
            DateOnly date = from;
            while (days > 0)
            {
                date = date.AddDays(1);
                if (IsWorkingDay(date))
                    days--;
            }
            return date;
        }

        public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive) => throw new NotSupportedException();
    }
}
