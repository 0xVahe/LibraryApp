using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>Reservation operations of the Borrowing module.</summary>
public interface IReservationService
{
    /// <summary>Creates a waiting reservation when the employee and book satisfy BOR-07.</summary>
    Task<ReservationDto> CreateAsync(Guid bookId, Guid staffMemberId, CancellationToken ct = default);
}
