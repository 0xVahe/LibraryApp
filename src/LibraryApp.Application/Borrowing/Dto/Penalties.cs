using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>A penalty record (BOR-05, BOR-08); the entity itself is never returned.</summary>
/// <param name="PenaltyId">The penalty record.</param>
/// <param name="StaffMemberId">The staff member who got the points.</param>
/// <param name="LoanId">The loan that gave the points (late, damaged or lost copy).</param>
/// <param name="Points">Points of this record, 0..10, by the penalty-point policy.</param>
/// <param name="Reason">Why the points were given, e.g. "Late return: 3 overdue working day(s)."</param>
/// <param name="Status">Active (counts), Expired (after its expiry) or Waived (cancelled by a librarian).</param>
/// <param name="CreatedAtUtc">When the points were given (UTC).</param>
/// <param name="ExpiresAtUtc">When the record stops counting (UTC): 90 days after it was given by default.</param>
/// <param name="WaivedReason">The reason of the waiver when Waived, otherwise null.</param>
public sealed record PenaltyDto(
    Guid PenaltyId,
    Guid StaffMemberId,
    Guid LoanId,
    int Points,
    string Reason,
    PenaltyRecordStatus Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    string? WaivedReason);

/// <summary>Waiving a penalty record (BOR-08).</summary>
/// <param name="Reason">Why the points are waived; required, at most 500 characters.</param>
public sealed record WaivePenaltyRequest(string Reason);
