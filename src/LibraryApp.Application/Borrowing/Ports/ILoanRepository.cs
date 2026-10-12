using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>
/// Storage of loans. It only stages changes; saving and committing belong to the unit of work.
/// </summary>
public interface ILoanRepository
{
    /// <summary>
    /// Stages a new loan to be saved.
    /// </summary>
    Task AddAsync(Loan loan, CancellationToken ct = default);

    /// <summary>
    /// Counts the open loans (Active or Overdue) of one employee.
    /// Used to compare against MaxLoans, because Staff does not know our loans.
    /// </summary>
    Task<int> CountOpenLoansAsync(Guid staffMemberId, CancellationToken ct = default);

    /// <summary>The loan with this id, read only; null when there is none.</summary>
    Task<Loan?> FindAsync(Guid loanId, CancellationToken ct = default);

    /// <summary>Copy ids of the employee's open (Active or Overdue) loans (BOR-07, BR-BOR-2).</summary>
    Task<IReadOnlyList<Guid>> GetOpenLoanCopyIdsAsync(Guid staffMemberId, CancellationToken ct = default);
}
