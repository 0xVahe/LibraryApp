namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>A place in the queue of a book (BOR-07).</summary>
/// <param name="BookId">The book to reserve.</param>
/// <param name="StaffMemberId">
/// The staff member the reservation is for. A Librarian or Library Administrator names them; a Staff Member reserves for
/// themselves and may leave it out (NFR-03).
/// </param>
public sealed record CreateReservationRequest(Guid BookId, Guid? StaffMemberId = null);
