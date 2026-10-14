using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Events;

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
    IIdGenerator idGenerator) : IReservationQueueService
{
    /// <summary>BOR-07: a ReadyForPickup reservation waits until the end of the 3rd working day after today (BQ-BA-12).</summary>
    public const int PickupWorkingDays = 3;

    public async Task OfferCopyAsync(Guid copyId, Guid bookId, CancellationToken ct = default)
    {
        // The queue of the book changes one request at a time: the same lock as creating a reservation.
        await applicationLock.AcquireAsync(BorrowingLocks.Book(bookId), ct);

        IReadOnlyList<Reservation> waiting = await reservations.GetWaitingForBookAsync(bookId, ct);
        if (waiting.Count == 0)
            return;   // nobody waits: the copy stays as the caller left it

        // The copy is not read back here: a new copy is not saved yet when Catalog calls (the caller's unit of work saves
        // it together with this change), and SetCopyStatusAsync finds it in the same DbContext.
        DateTime readyUntilUtc = clock.EndOfLocalDayUtc(calendar.AddWorkingDays(clock.LocalToday, PickupWorkingDays));
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
