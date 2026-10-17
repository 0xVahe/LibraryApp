using LibraryApp.Application.Borrowing.Ports;
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

    public async Task AddAsync(
        PenaltyRecord penalty,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(penalty);

        await db.Set<PenaltyRecord>().AddAsync(penalty, ct);
    }
}