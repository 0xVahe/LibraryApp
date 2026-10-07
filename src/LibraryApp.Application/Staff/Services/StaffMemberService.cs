using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

public sealed class StaffMemberService(IStaffMemberRepository staffMembers) : IStaffMemberService
{
    public async Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        var status = await staffMembers.GetStatusAsync(staffMemberId, ct);

        return status is null
            ? EligibilityResult.Denied(StaffErrors.NotFound)
            : BorrowingEligibilityPolicy.Evaluate(status.Value);
    }

    public async Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default)
    {
        var info = await staffMembers.GetLendingInfoAsync(staffMemberId, ct)
                   ?? throw new DomainException(StaffErrors.NotFound);

        return BorrowingEligibilityPolicy.ApplyStatus(info.Status, info.CategoryRules);
    }

    public async Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) =>
        await staffMembers.GetStatusAsync(staffMemberId, ct)
        ?? throw new DomainException(StaffErrors.NotFound);

    // reason: not stored yet; it goes to the audit log / StaffStatusChanged published by the caller.
    public async Task SetStatusAsync(
        Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default)
    {
        var member = await staffMembers.GetByIdAsync(staffMemberId, ct)
                     ?? throw new DomainException(StaffErrors.NotFound);

        if (member.Status == status)
            return;

        member.ChangeStatus(status); // refuses Deactivated and a Deactivated member
    }
}