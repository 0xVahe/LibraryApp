using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="IBorrowingReadService"/> for Staff (BR-MEM-4). Read only: no tracking and no transaction of its own, so
/// a caller uses it inside its unit of work after taking <see cref="BorrowingLocks.Staff"/>. The book of a loan comes
/// from the foreign key Loans → BookCopies in the same query: no Catalog service call and no query per loan.
/// </summary>
public sealed class BorrowingReadService(LibraryDbContext db) : IBorrowingReadService
{
    public async Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
        await (from loan in db.Loans.AsNoTracking()
               join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
               where loan.StaffMemberId == staffMemberId
                     && (loan.Status == LoanStatus.Active || loan.Status == LoanStatus.Overdue)
               orderby loan.DueAtUtc
               select new OpenLoanInfo(loan.Id, loan.StaffMemberId, loan.BookCopyId, copy.BookId, loan.DueAtUtc, loan.Status))
            .ToListAsync(ct);
}
