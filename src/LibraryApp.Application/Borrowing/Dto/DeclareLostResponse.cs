using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// The result of declaring a borrowed copy lost.
/// </summary>
public sealed record DeclareLostResponse(
    Guid LoanId,
    DateTime DeclaredAtUtc,
    LoanStatus Status,
    int PenaltyPointsApplied);