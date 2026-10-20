namespace LibraryApp.Application.Staff.Dto;

/// <summary>Only the caller's first and last name can be changed through the self-profile endpoint.</summary>
public sealed record UpdateMyProfileRequest(string FirstName, string LastName);

/// <summary>MEM-08 "My library": the caller's own loans, reservations and active penalty points (NFR-03).</summary>
/// <param name="ActiveLoans">Active and Overdue loans, earliest due date first.</param>
/// <param name="Reservations">Waiting and ReadyForPickup reservations, oldest first.</param>
/// <param name="ActivePenaltyPoints">The sum of <paramref name="ActivePenalties"/>.</param>
/// <param name="ActivePenalties">Penalty records that have not expired yet (BR-BOR-6), oldest first.</param>
public sealed record MyLibraryDto(
    MyProfileDto Profile,
    IReadOnlyList<MyLoanDto> ActiveLoans,
    IReadOnlyList<MyReservationDto> Reservations,
    int ActivePenaltyPoints,
    IReadOnlyList<MyPenaltyDto> ActivePenalties);

/// <summary>Profile summary of the caller.</summary>
/// <param name="Status">Active, Restricted or Suspended.</param>
public sealed record MyProfileDto(
    Guid Id,
    string FirstName,
    string LastName,
    string CorporateEmail,
    string DepartmentName,
    string Position,
    string StaffCategoryName,
    string Status);

/// <param name="Title">Null when Catalog cannot resolve the book.</param>
/// <param name="Barcode">Null when Catalog cannot resolve the copy.</param>
/// <param name="IsOverdue">True when the loan is Overdue or its due date has passed.</param>
public sealed record MyLoanDto(
    Guid LoanId,
    Guid BookId,
    string? Title,
    string? Barcode,
    DateTime IssuedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue,
    int RenewalCount);

/// <param name="Title">Null when Catalog cannot resolve the book.</param>
/// <param name="Status">Waiting or ReadyForPickup.</param>
/// <param name="QueuePosition">1..n while Waiting, 0 when ReadyForPickup.</param>
/// <param name="ReadyUntilUtc">The pickup deadline while ReadyForPickup, otherwise null.</param>
public sealed record MyReservationDto(
    Guid ReservationId,
    Guid BookId,
    string? Title,
    string Status,
    int QueuePosition,
    DateTime? ReadyUntilUtc,
    DateTime CreatedAtUtc);

/// <param name="Title">The book of the loan behind the penalty; null when Catalog cannot resolve it.</param>
public sealed record MyPenaltyDto(
    Guid PenaltyId,
    Guid BookId,
    string? Title,
    int Points,
    string Reason,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);
