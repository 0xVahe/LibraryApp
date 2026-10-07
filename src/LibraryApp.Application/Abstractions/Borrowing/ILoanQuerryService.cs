namespace LibraryApp.Application.Abstractions.Borrowing;

public interface ILoanQueryService
{
    // CAT-08: earliest due date of active loans for this book.
    // Returns null when no matching loans exist.
    Task<DateTime?> GetNextDueDateAsync(
        Guid bookId,
        CancellationToken ct = default);
}