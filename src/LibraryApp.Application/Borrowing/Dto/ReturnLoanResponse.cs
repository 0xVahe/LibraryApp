using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>The result of taking a copy back (BOR-03, BOR-05).</summary>
/// <param name="LoanId">The returned loan.</param>
/// <param name="ReturnedAtUtc">When the copy was taken back (UTC).</param>
/// <param name="Status">Always Returned.</param>
/// <param name="OverdueWorkingDays">Working days the copy came back late (BR-BOR-5); 0 when on time.</param>
/// <param name="PenaltyPointsApplied">Points added by this return (late and damaged, by the penalty-point policy); 0 when none.</param>
public sealed record ReturnLoanResponse(
    Guid LoanId,
    DateTime ReturnedAtUtc,
    LoanStatus Status,
    int OverdueWorkingDays,
    int PenaltyPointsApplied);
