using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;

/// <summary>
/// EF Core storage for penalties.
/// Never saves or opens its own transaction.
/// </summary>
public sealed class PenaltyRepository(LibraryDbContext db)
    : IPenaltyRepository
{
    public async Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        return await db.Set<PenaltyRecord>()
            .AsTracking()
            .Where(penalty => penalty.StaffMemberId == staffMemberId)
            .OrderBy(penalty => penalty.CreatedAtUtc)
            .ThenBy(penalty => penalty.Id)
            .ToListAsync(ct);
    }

    public Task<PenaltyRecord?> FindAsync(Guid penaltyId, CancellationToken ct = default) =>
        db.Set<PenaltyRecord>().AsNoTracking().SingleOrDefaultAsync(penalty => penalty.Id == penaltyId, ct);

    public Task<PenaltyRecord?> GetForUpdateAsync(Guid penaltyId, CancellationToken ct = default) =>
        db.Set<PenaltyRecord>().AsTracking().SingleOrDefaultAsync(penalty => penalty.Id == penaltyId, ct);

    public async Task<PagedResult<PenaltyRecord>> PageForStaffAsync(
        Guid staffMemberId,
        PenaltyRecordStatus? status,
        PageRequest page,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        IQueryable<PenaltyRecord> query = db.Set<PenaltyRecord>()
            .AsNoTracking()
            .Where(penalty => penalty.StaffMemberId == staffMemberId);

        if (status is { } wanted)
            query = query.Where(penalty => penalty.Status == wanted);

        int total = await query.CountAsync(ct);

        List<PenaltyRecord> items = await query
            .OrderByDescending(penalty => penalty.CreatedAtUtc)
            .ThenByDescending(penalty => penalty.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return new PagedResult<PenaltyRecord>(items, total, page.Page, page.PageSize);
    }

    public async Task AddAsync(
        PenaltyRecord penalty,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(penalty);

        await db.Set<PenaltyRecord>().AddAsync(penalty, ct);
    }
}