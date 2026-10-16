using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// EF Core implementation of <see cref="ILoanRepository"/>.
/// It never calls SaveChanges: the unit of work saves and commits for the whole business operation.
/// </summary>
public sealed class LoanRepository(LibraryDbContext db) : ILoanRepository
{
    /// <inheritdoc />
    public Task<Loan?> GetForUpdateAsync(
        Guid loanId,
        CancellationToken ct = default) =>
        db.Set<Loan>()
            .AsTracking()
            .SingleOrDefaultAsync(loan => loan.Id == loanId, ct);
    
    
    /// <inheritdoc />
    public async Task AddAsync(Loan loan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(loan);

        // Only marks the loan as "to be inserted". The INSERT runs when the unit of work saves.
        await db.Set<Loan>().AddAsync(loan, ct);
    }

    /// <inheritdoc />
    public Task<int> CountOpenLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
        db.Set<Loan>()
            .AsNoTracking() // read-only query: EF does not need to track these rows
            .CountAsync(
                // Open = Active or Overdue. We spell out the two statuses because EF cannot
                // translate the C# property Loan.IsOpen into SQL.
                l => l.StaffMemberId == staffMemberId
                     && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue),
                ct);

    /// <inheritdoc />
    public Task<Loan?> FindAsync(Guid loanId, CancellationToken ct = default) =>
        db.Set<Loan>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == loanId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetOpenLoanCopyIdsAsync(Guid staffMemberId, CancellationToken ct = default) =>
        await db.Set<Loan>()
            .AsNoTracking()
            .Where(l => l.StaffMemberId == staffMemberId
                        && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue))
            .Select(l => l.BookCopyId)
            .ToListAsync(ct);
}
