using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="ILoanQueryService"/> for Catalog's book details (CAT-08). One query over the foreign key
/// Loans → BookCopies, no Catalog service call: Catalog can call it while Borrowing calls Catalog.
/// </summary>
public sealed class LoanQueryService(LibraryDbContext db) : ILoanQueryService
{
    public Task<DateTime?> GetNextDueDateAsync(Guid bookId, CancellationToken ct = default) =>
        (from loan in db.Loans.AsNoTracking()
         join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
         where copy.BookId == bookId && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
         select (DateTime?)loan.DueAtUtc)
        .MinAsync(ct);
}
