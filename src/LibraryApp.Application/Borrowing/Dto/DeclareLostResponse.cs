using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>The result of declaring the copy of a loan lost (BOR-09).</summary>
/// <param name="LoanId">The loan whose copy was lost.</param>
/// <param name="DeclaredAtUtc">When the copy was declared lost (UTC).</param>
/// <param name="Status">Always Lost.</param>
/// <param name="PenaltyPointsApplied">The lost points of the penalty-point policy that were added (default 10).</param>
public sealed record DeclareLostResponse(
    Guid LoanId,
    DateTime DeclaredAtUtc,
    LoanStatus Status,
    int PenaltyPointsApplied);
