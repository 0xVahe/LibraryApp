using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// Finds candidates for the daily Borrowing job.
/// Does not modify entities, save changes, or open transactions.
/// </summary>
public sealed class BorrowingDailyJobQuery(LibraryDbContext db)
    : IBorrowingDailyJobQuery
{
    public async Task<IReadOnlyList<Guid>> GetActiveLoanIdsDueBeforeAsync(
        DateTime cutoffUtc,
        CancellationToken ct = default)
    {
        if (cutoffUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "The overdue cutoff must be UTC.",
                nameof(cutoffUtc));
        }

        return await db.Loans
            .AsNoTracking()
            .Where(loan =>
                loan.Status == LoanStatus.Active
                && loan.DueAtUtc < cutoffUtc)
            .OrderBy(loan => loan.DueAtUtc)
            .ThenBy(loan => loan.Id)
            .Select(loan => loan.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetStaffIdsWithPenaltyHistoryAsync(
        CancellationToken ct = default)
    {
        return await db.PenaltyRecords
            .AsNoTracking()
            .Where(penalty => penalty.Status == PenaltyRecordStatus.Active)
            .Select(penalty => penalty.StaffMemberId)
            .Distinct()
            .OrderBy(staffMemberId => staffMemberId)
            .ToListAsync(ct);
    }
}