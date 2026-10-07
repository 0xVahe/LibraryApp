using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

public interface IStaffMemberRepository
{
    /// <summary>Tracked: changes are saved by the unit of work.</summary>
    Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<StaffMemberStatus?> GetStatusAsync(Guid id, CancellationToken ct);

    Task<StaffLendingInfo?> GetLendingInfoAsync(Guid id, CancellationToken ct);
}

/// <summary>Status of a staff member and the limits of their category, read in one query.</summary>
public sealed record StaffLendingInfo(StaffMemberStatus Status, LendingRules CategoryRules);