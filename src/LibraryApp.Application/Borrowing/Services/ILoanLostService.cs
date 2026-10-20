using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Declares a loan lost and applies the corresponding penalty.
/// </summary>
public interface ILoanLostService
{
    Task<DeclareLostResponse> DeclareLostAsync(
        Guid loanId,
        CancellationToken ct = default);
}