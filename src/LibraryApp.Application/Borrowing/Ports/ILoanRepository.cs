using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Ports;

/// <summary>
/// Storage of loans. It only stages changes; saving and committing belong to the unit of work.
/// Only the methods Task 3 needs are here. Later tasks will add more (get by id, history, and so on).
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

    /// <summary>Gets copy ids for the employee's Active or Overdue loans (BOR-07).</summary>
    Task<IReadOnlyList<Guid>> GetOpenLoanCopyIdsAsync(Guid staffMemberId, CancellationToken ct = default);
}
