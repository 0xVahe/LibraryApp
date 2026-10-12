using LibraryApp.Application.Abstractions.Borrowing;

namespace LibraryApp.Application.Tests.Catalog;

/// <summary>Records every offer to the reservation queue; can also act like Borrowing with the offered copy.</summary>
internal sealed class FakeReservationQueue : IReservationQueueService
{
    public List<(Guid CopyId, Guid BookId)> Offers { get; } = [];

    /// <summary>What "Borrowing" does with the offered copy, for example reserve it. Nothing by default.</summary>
    public Func<Guid, Task>? OnOffer { get; set; }

    public async Task OfferCopyAsync(Guid copyId, Guid bookId, CancellationToken ct = default)
    {
        Offers.Add((copyId, bookId));

        if (OnOffer is not null)
            await OnOffer(copyId);
    }
}
