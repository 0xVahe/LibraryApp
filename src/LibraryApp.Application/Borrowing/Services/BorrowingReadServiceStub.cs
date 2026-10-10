using LibraryApp.Application.Abstractions.Borrowing;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>Temporary empty provider permitted by S1-04.2. Group 3 replaces it before real issuance is enabled.</summary>
public sealed class BorrowingReadServiceStub : IBorrowingReadService
{
    public Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OpenLoanInfo>>([]);
    }
}
