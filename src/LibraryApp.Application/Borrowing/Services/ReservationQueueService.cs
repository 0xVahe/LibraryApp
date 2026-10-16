using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Events;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// The reservation queue of a book (BOR-07, BQ-G1-10): a copy that becomes free goes to the first Waiting reservation.
/// Runs inside the caller's unit of work (Catalog's, when a copy is added or a repair ends) and opens no transaction of
/// its own, so the copy, the reservation and the event are saved together or not at all.
/// </summary>
public sealed class ReservationQueueService(
    IReservationRepository reservations,
    ICatalogService catalog,
    IApplicationLock applicationLock,
    IEventBus events,
    IWorkingDayCalendar calendar,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<BorrowingOptions> options) : IReservationQueueService
{
    /// <summary>
    /// BOR-07, BQ-BA-12: a ReadyForPickup reservation waits until the end of the last pickup working day. By default the
    /// day the copy is held does not count (held on Friday with 3 days → end of Wednesday); with
    /// <see cref="ReservationOptions.PickupCountsReleaseDay"/> a working release day is the first pickup day.
    /// The seed uses the same rule.
    /// </summary>
    public static DateTime ReadyUntilUtc(ReservationOptions settings, IWorkingDayCalendar calendar, IClock clock)
    {
        DateOnly today = clock.LocalToday;
        int daysToAdd = settings.PickupCountsReleaseDay && calendar.IsWorkingDay(today)
            ? settings.PickupWorkingDays - 1
            : settings.PickupWorkingDays;
        return clock.EndOfLocalDayUtc(calendar.AddWorkingDays(today, daysToAdd));
    }

    public async Task OfferCopyAsync(Guid copyId, Guid bookId, CancellationToken ct = default)
    {
        // The queue of the book changes one request at a time: the same lock as creating a reservation.
        await applicationLock.AcquireAsync(BorrowingLocks.Book(bookId), ct);

        IReadOnlyList<Reservation> waiting = await reservations.GetWaitingForBookAsync(bookId, ct);
        if (waiting.Count == 0)
            return;   // nobody waits: the copy stays as the caller left it

        // The copy is not read back here: a new copy is not saved yet when Catalog calls (the caller's unit of work saves
        // it together with this change), and SetCopyStatusAsync finds it in the same DbContext.
        DateTime readyUntilUtc = ReadyUntilUtc(options.Value.Reservations, calendar, clock);
        Reservation first = waiting[0];
        first.MarkReady(copyId, readyUntilUtc);

        // The others move up one place: positions stay 1..n without gaps (G3-03).
        for (int index = 1; index < waiting.Count; index++)
            waiting[index].SetQueuePosition(index);

        // Catalog's transition rules apply (Available → Reserved); only Borrowing sets Reserved (BR-CAT-4).
        await catalog.SetCopyStatusAsync(copyId, BookCopyStatus.Reserved, ct);

        await events.PublishAsync(new ReservationReady(
            idGenerator.NewId(),
            clock.UtcNow,
            first.Id,
            bookId,
            first.StaffMemberId,
            readyUntilUtc), ct);
    }
}
