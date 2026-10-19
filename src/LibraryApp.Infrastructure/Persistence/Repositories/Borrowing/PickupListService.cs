using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="IPickupListService"/>: two reads of Reservations — the total, then the page — with no tracking and no
/// transaction, as every paged list of the system (SD-14). A reservation that changes between the two reads can make the
/// total not match the page for that request; the next request is exact. A reservation whose deadline has
/// passed stays in the list until the daily job expires it (BOR-11), so the desk still sees it.
/// </summary>
public sealed class PickupListService(LibraryDbContext db) : IPickupListService
{
    public async Task<PagedResult<ReadyForPickupDto>> GetReadyForPickupAsync(PageRequest page, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var ready = db.Reservations.AsNoTracking().Where(r => r.Status == ReservationStatus.ReadyForPickup);

        int total = await ready.CountAsync(ct);

        // The id breaks ties, so a row never moves between pages.
        List<ReadyForPickupDto> items = await ready
            .OrderBy(r => r.ReadyUntilUtc)
            .ThenBy(r => r.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(r => new ReadyForPickupDto(r.Id, r.BookId, r.CopyId!.Value, r.StaffMemberId, r.ReadyUntilUtc!.Value))
            .ToListAsync(ct);

        return new PagedResult<ReadyForPickupDto>(items, total, page.Page, page.PageSize);
    }
}
