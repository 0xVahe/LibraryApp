using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// The result of successfully returning a loan.
/// </summary>
public sealed record ReturnLoanResponse(
    Guid LoanId,
    DateTime ReturnedAtUtc,
    LoanStatus Status,
    int OverdueWorkingDays,
    int PenaltyPointsApplied);