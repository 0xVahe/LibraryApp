namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>
/// The reservation queue of a book (BOR-07), as Catalog uses it. Provider: Group 3 (Borrowing).
/// Consumer: Group 1 (Catalog).
/// </summary>
public interface IReservationQueueService
{
    /// <summary>
    /// Catalog calls this when a copy becomes available outside a loan: a new copy is added, or a librarian ends a repair.
    /// If someone is waiting for the book, the first Waiting reservation becomes ReadyForPickup for this copy, the copy is
    /// set Reserved through <c>ICatalogService.SetCopyStatusAsync</c>, and ReservationReady is published through
    /// <c>IEventBus</c>. If nobody is waiting, nothing changes.
    /// <para>
    /// Only changes data inside the caller's <c>IUnitOfWork</c>: no SaveChanges and no own transaction (the unit of work
    /// refuses a nested one), so the copy and the reservation are saved together or not at all.
    /// </para>
    /// </summary>
    Task OfferCopyAsync(Guid copyId, Guid bookId, CancellationToken ct = default);
}
