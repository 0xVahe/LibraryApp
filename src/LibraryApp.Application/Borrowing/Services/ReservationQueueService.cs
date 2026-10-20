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

    public async Task OfferCopyAsync(
        Guid copyId,
        Guid bookId,
        CancellationToken ct = default)
    {
        await applicationLock.AcquireAsync(BorrowingLocks.Book(bookId), ct);
        await TryOfferCopyUnderBookLockAsync(copyId, bookId, BookCopyStatus.Available, ct);
    }

    /// <summary>
    /// Offers the copy to the first waiting reservation.
    /// Returns false when nobody is waiting, leaving the copy unchanged.
    /// The caller must already hold the book lock and run inside the transaction that saves the surrounding operation.
    /// </summary>
    internal async Task<bool> TryOfferCopyUnderBookLockAsync(
        Guid copyId,
        Guid bookId,
        BookCopyStatus currentCopyStatus,
        CancellationToken ct = default)
    {
        IReadOnlyList<Reservation> waiting =
            await reservations.GetWaitingForBookAsync(bookId, ct);

        if (waiting.Count == 0)
            return false;

        DateTime readyUntilUtc =
            ReadyUntilUtc(options.Value.Reservations, calendar, clock);

        Reservation first = waiting[0];
        first.MarkReady(copyId, readyUntilUtc);

        // Remaining positions become 1, 2, 3, ...
        for (int index = 1; index < waiting.Count; index++)
            waiting[index].SetQueuePosition(index);

        // Supports newly available copies, returned OnLoan copies, and a copy already Reserved for the member who
        // just collected a different copy. The latter needs no status transition. A new copy is not saved yet when
        // Catalog calls; SetCopyStatusAsync finds it in the same DbContext. Only Borrowing sets Reserved (BR-CAT-4).
        if (currentCopyStatus != BookCopyStatus.Reserved)
            await catalog.SetCopyStatusAsync(copyId, BookCopyStatus.Reserved, ct);

        await events.PublishAsync(
            new ReservationReady(
                idGenerator.NewId(),
                clock.UtcNow,
                first.Id,
                bookId,
                first.StaffMemberId,
                readyUntilUtc),
            ct);

        return true;
    }
    /// <summary>
    /// BOR-12, G3-26: cancels a Waiting or ReadyForPickup reservation. A ReadyForPickup one releases its held copy to
    /// the first Waiting reservation (<c>ReservationReady</c>) or makes it Available when nobody waits; after a Waiting
    /// one the members behind it move up (positions stay 1..n). The caller holds the book lock (and the staff lock
    /// first, where it takes both), has loaded the reservation for update after taking the locks, and runs inside the
    /// unit of work that saves the change.
    /// </summary>
    internal async Task CancelUnderLocksAsync(Reservation reservation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        Guid? heldCopyId = reservation.Status == ReservationStatus.ReadyForPickup
            ? reservation.CopyId ?? throw new InvalidOperationException("A ready reservation must have a held copy.")
            : null;

        reservation.Cancel();

        // Written now, inside the transaction: the queue query below reads only persisted Waiting rows, and the copy
        // may be held for the next member only after this reservation has left ReadyForPickup
        // (UX_Reservations_ReadyForPickup_CopyId; see IReservationRepository.FlushAsync).
        await reservations.FlushAsync(ct);

        if (heldCopyId is { } copyId)
        {
            bool offered = await TryOfferCopyUnderBookLockAsync(copyId, reservation.BookId, BookCopyStatus.Reserved, ct);

            if (!offered)
                await catalog.SetCopyStatusAsync(copyId, BookCopyStatus.Available, ct);

            return;
        }

        // The members behind the cancelled one move up: positions 1..n without gaps, in queue order.
        IReadOnlyList<Reservation> waiting = await reservations.GetWaitingForBookAsync(reservation.BookId, ct);
        for (int index = 0; index < waiting.Count; index++)
        {
            if (waiting[index].QueuePosition != index + 1)
                waiting[index].SetQueuePosition(index + 1);
        }
    }
}
