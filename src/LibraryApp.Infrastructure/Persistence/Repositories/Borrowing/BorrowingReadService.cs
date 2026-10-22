using System.Linq.Expressions;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// <see cref="IBorrowingReadService"/> for Staff (BR-MEM-4, MEM-08) and Notifications (NOT-05). Read only: no tracking
/// and no transaction of its own, so a caller uses it inside its unit of work after taking <see cref="BorrowingLocks.Staff"/>.
/// Borrowing reads only its own tables; the book of a loan's copy comes from Catalog through
/// <see cref="ICatalogService.GetCopiesAsync"/>, one call for the whole list.
/// </summary>
public sealed class BorrowingReadService(LibraryDbContext db, ICatalogService catalog, IClock clock) : IBorrowingReadService
{
    public Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
        OpenLoansAsync(loan => loan.StaffMemberId == staffMemberId, ct);

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

        // Points stop counting at ExpiresAtUtc even before the daily job marks the record Expired.
        DateTime now = clock.UtcNow;
        var penalties = await (
                from p in db.PenaltyRecords.AsNoTracking()
                    .Where(p => p.StaffMemberId == staffMemberId && p.Status == PenaltyRecordStatus.Active && p.ExpiresAtUtc > now)
                join loan in db.Loans.AsNoTracking() on p.LoanId equals loan.Id
                orderby p.CreatedAtUtc
                select new { Penalty = p, loan.BookCopyId })
            .ToListAsync(ct);

        // The book through the penalty's loan and that loan's copy.
        IReadOnlyDictionary<Guid, Guid> bookOfCopy = await BooksOfCopiesAsync(penalties.Select(x => x.BookCopyId), ct);
        List<ActivePenaltyInfo> activePenalties = penalties
            .Where(x => bookOfCopy.ContainsKey(x.BookCopyId))
            .Select(x => new ActivePenaltyInfo(x.Penalty.Id, x.Penalty.LoanId, bookOfCopy[x.BookCopyId], x.Penalty.Points,
                x.Penalty.Reason, x.Penalty.CreatedAtUtc, x.Penalty.ExpiresAtUtc))
            .ToList();

        return new StaffBorrowingSummary(staffMemberId, loans, reservations, activePenalties.Sum(p => p.Points), activePenalties);
    }

    public Task<IReadOnlyList<OpenLoanInfo>> GetLoansDueOnAsync(DateOnly localDate, CancellationToken ct = default)
    {
        DateTime from = clock.StartOfLocalDayUtc(localDate);
        DateTime to = clock.EndOfLocalDayUtc(localDate);

        return OpenLoansAsync(loan => loan.DueAtUtc >= from && loan.DueAtUtc <= to, ct);
    }

    public Task<IReadOnlyList<OpenLoanInfo>> GetOverdueLoansAsync(CancellationToken ct = default) =>
        OpenLoansAsync(loan => loan.Status == LoanStatus.Overdue, ct);

    // Open = Active or Overdue; Returned and Lost are closed. The filter runs on the loan, so EF translates it to SQL;
    // the earliest due date comes first.
    private async Task<IReadOnlyList<OpenLoanInfo>> OpenLoansAsync(Expression<Func<Loan, bool>> filter, CancellationToken ct)
    {
        List<Loan> loans = await db.Loans.AsNoTracking()
            .Where(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue)
            .Where(filter)
            .OrderBy(l => l.DueAtUtc)
            .ToListAsync(ct);

        IReadOnlyDictionary<Guid, Guid> bookOfCopy = await BooksOfCopiesAsync(loans.Select(l => l.BookCopyId), ct);
        return loans
            .Where(l => bookOfCopy.ContainsKey(l.BookCopyId))
            .Select(l => new OpenLoanInfo(l.Id, l.StaffMemberId, l.BookCopyId, bookOfCopy[l.BookCopyId], l.DueAtUtc, l.Status,
                l.IssuedAtUtc, l.RenewalCount))
            .ToList();
    }

    // Copy id → book id from Catalog. A loan always points to an existing copy (foreign key, Restrict), so every copy is
    // found; Catalog returns copies of every status and of soft-deleted books.
    private async Task<IReadOnlyDictionary<Guid, Guid>> BooksOfCopiesAsync(IEnumerable<Guid> copyIds, CancellationToken ct)
    {
        Guid[] ids = copyIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, Guid>();

        IReadOnlyList<BookCopyDto> copies = await catalog.GetCopiesAsync(ids, ct);
        return copies.ToDictionary(copy => copy.Id, copy => copy.BookId);
    }
}
