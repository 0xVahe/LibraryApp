using LibraryApp.Application.Abstractions.Borrowing;

namespace LibraryApp.Application.Catalog.Services;

/// <summary>
/// Temporary provider of <see cref="IReservationQueueService"/> until Group 3 registers the real one: it behaves as if
/// nobody is waiting, so nothing changes. Registered with TryAddScoped in AddCatalogModule; the real implementation is
/// registered with AddScoped (or this class is deleted). Remove before the code freeze.
/// </summary>
public sealed class ReservationQueueServiceStub : IReservationQueueService
{
    public Task OfferCopyAsync(Guid copyId, Guid bookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
