using System.Linq.Expressions;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="IBorrowingReadService"/> for Staff (BR-MEM-4, MEM-08) and Notifications (NOT-05). Read only: no tracking
/// and no transaction of its own, so a caller uses it inside its unit of work after taking <see cref="BorrowingLocks.Staff"/>.
/// The book of a loan comes from the foreign key Loans → BookCopies in the same query: no Catalog service call and no
/// query per loan.
/// </summary>
public sealed class BorrowingReadService(LibraryDbContext db, IClock clock) : IBorrowingReadService
{
    public async Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
        await OpenLoans(loan => loan.StaffMemberId == staffMemberId).ToListAsync(ct);

    public async Task<StaffBorrowingSummary> GetStaffSummaryAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        IReadOnlyList<OpenLoanInfo> loans = await GetActiveLoansAsync(staffMemberId, ct);

        List<OpenReservationInfo> reservations = await db.Reservations
            .AsNoTracking()
            .Where(r => r.StaffMemberId == staffMemberId
                        && (r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.ReadyForPickup))
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new OpenReservationInfo(r.Id, r.BookId, r.Status, r.QueuePosition, r.ReadyUntilUtc, r.CreatedAtUtc))
            .ToListAsync(ct);

        // Points stop counting at ExpiresAtUtc even before the daily job marks the record Expired (BQ-G2-11, note 2).
        DateTime now = clock.UtcNow;
        // The book through the penalty's loan and that loan's copy (foreign keys), in the same query.
        List<ActivePenaltyInfo> penalties = await (
                from p in db.PenaltyRecords.AsNoTracking()
                    .Where(p => p.StaffMemberId == staffMemberId && p.Status == PenaltyRecordStatus.Active && p.ExpiresAtUtc > now)
                join loan in db.Loans.AsNoTracking() on p.LoanId equals loan.Id
                join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
                orderby p.CreatedAtUtc
                select new ActivePenaltyInfo(p.Id, p.LoanId, copy.BookId, p.Points, p.Reason, p.CreatedAtUtc, p.ExpiresAtUtc))
            .ToListAsync(ct);

        return new StaffBorrowingSummary(staffMemberId, loans, reservations, penalties.Sum(p => p.Points), penalties);
    }

    public async Task<IReadOnlyList<OpenLoanInfo>> GetLoansDueOnAsync(DateOnly localDate, CancellationToken ct = default)
    {
        DateTime from = clock.StartOfLocalDayUtc(localDate);
        DateTime to = clock.EndOfLocalDayUtc(localDate);

        return await OpenLoans(loan => loan.DueAtUtc >= from && loan.DueAtUtc <= to).ToListAsync(ct);
    }

    // Open = Active or Overdue; Returned and Lost are closed. The filter runs on the loan, before the projection, so
    // EF translates it to SQL; the earliest due date comes first.
    private IQueryable<OpenLoanInfo> OpenLoans(Expression<Func<Loan, bool>> filter) =>
        from loan in db.Loans.AsNoTracking()
            .Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue)
            .Where(filter)
        join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id
        orderby loan.DueAtUtc
        select new OpenLoanInfo(loan.Id, loan.StaffMemberId, loan.BookCopyId, copy.BookId, loan.DueAtUtc, loan.Status,
            loan.IssuedAtUtc, loan.RenewalCount);
}
