using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Borrowing data of one staff member for MEM-08 "My library" (BQ-G2-11).</summary>
/// <param name="ActiveLoans">Active and Overdue loans, earliest due date first.</param>
/// <param name="Reservations">Waiting and ReadyForPickup reservations, oldest first.</param>
/// <param name="ActivePenaltyPoints">The sum of <paramref name="ActivePenalties"/>.</param>
/// <param name="ActivePenalties">Active penalty records that have not expired yet (BR-BOR-6), oldest first.</param>
public sealed record StaffBorrowingSummary(
    Guid StaffMemberId,
    IReadOnlyList<OpenLoanInfo> ActiveLoans,
    IReadOnlyList<OpenReservationInfo> Reservations,
    int ActivePenaltyPoints,
    IReadOnlyList<ActivePenaltyInfo> ActivePenalties);

/// <param name="QueuePosition">1..n while Waiting, 0 when ReadyForPickup.</param>
/// <param name="ReadyUntilUtc">The pickup deadline while ReadyForPickup, otherwise null.</param>
public sealed record OpenReservationInfo(
    Guid ReservationId,
    Guid BookId,
    ReservationStatus Status,
    int QueuePosition,
    DateTime? ReadyUntilUtc,
    DateTime CreatedAtUtc);

/// <param name="BookId">The book of the loan behind the penalty; that loan is usually returned, so it is not in ActiveLoans.</param>
public sealed record ActivePenaltyInfo(
    Guid PenaltyId,
    Guid LoanId,
    Guid BookId,
    int Points,
    string Reason,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);
