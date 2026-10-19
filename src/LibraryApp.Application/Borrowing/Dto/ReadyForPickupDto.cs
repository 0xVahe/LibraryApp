namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// A reservation whose copy is held at the desk (BOR-07, Loan desk "Waiting for pickup"). Ids only: the page adds the
/// title and barcode from Catalog and the name from Staff.
/// </summary>
public sealed record ReadyForPickupDto(
    Guid ReservationId,
    Guid BookId,
    Guid CopyId,
    Guid StaffMemberId,
    DateTime ReadyUntilUtc);
