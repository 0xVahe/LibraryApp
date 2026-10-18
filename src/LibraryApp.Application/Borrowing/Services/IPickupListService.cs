using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>The copies held for pickup, for the Loan desk (BOR-07). Read only, no transaction.</summary>
public interface IPickupListService
{
    /// <summary>ReadyForPickup reservations, the earliest pickup deadline first (SD-14 paging).</summary>
    Task<PagedResult<ReadyForPickupDto>> GetReadyForPickupAsync(PageRequest page, CancellationToken ct = default);
}
