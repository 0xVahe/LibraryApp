namespace LibraryApp.Application.Abstractions.Borrowing;

/// <summary>Loan data for Catalog (CAT-08). Provider: Borrowing (Group 3). Read only, no transaction of its own.</summary>
public interface ILoanQueryService
{
    /// <summary>
    /// The earliest due date (UTC) among the open loans (Active or Overdue) of the copies of this book; null when no
    /// copy is on loan. An Overdue loan counts: its due date is in the past, but the copy is still out.
    /// </summary>
    Task<DateTime?> GetNextDueDateAsync(
        Guid bookId,
        CancellationToken ct = default);
}