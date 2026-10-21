using LibraryApp.Application.Borrowing.Dto;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>Renews a loan (BOR-04, BR-BOR-1); a Staff Member renews only their own loans (NFR-03).</summary>
public interface ILoanRenewalService
{
    Task<LoanDto> RenewAsync(Guid loanId, CancellationToken ct = default);
}
