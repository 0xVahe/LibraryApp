using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>A penalty record (BOR-05, BOR-08); the entity itself is never returned.</summary>
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

/// <summary>Waiving a penalty record (BOR-08): the reason is mandatory.</summary>
public sealed record WaivePenaltyRequest(string Reason);
