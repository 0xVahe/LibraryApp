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
        await TryOfferCopyAsync(copyId, bookId, ct);
    }

    /// <summary>
    /// Offers the copy to the first waiting reservation.
    /// Returns false when nobody is waiting, leaving the copy unchanged.
    /// Runs inside the caller's transaction.
    /// </summary>
    internal async Task<bool> TryOfferCopyAsync(
        Guid copyId,
        Guid bookId,
        CancellationToken ct = default)
    {
        await applicationLock.AcquireAsync(
            BorrowingLocks.Book(bookId), ct);

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

        // Supports both newly available copies and returned OnLoan copies.
        await catalog.SetCopyStatusAsync(
            copyId, BookCopyStatus.Reserved, ct);

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
}
