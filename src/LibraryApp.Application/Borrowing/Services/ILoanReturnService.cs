using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Services;

public interface ILoanReturnService
{
    Task<ReturnLoanResponse> ReturnAsync(
        Guid loanId,
        ReturnLoanRequest request,
        CancellationToken ct = default);
}