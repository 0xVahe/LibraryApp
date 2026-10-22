using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

public sealed class FakeStaffMemberService : IStaffMemberService
{
    public EligibilityResult Eligibility { get; set; } =
        EligibilityResult.Allowed();

    public LendingRules Rules { get; set; } = new(
        MaxLoans: 3,
        LoanDays: 14,
        MaxRenewals: 2);

    public List<string> Calls { get; } = new();

    public Task<EligibilityResult> CanBorrowAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add(nameof(CanBorrowAsync));
        return Task.FromResult(Eligibility);
    }

    public Task<LendingRules> GetLendingRulesAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add(nameof(GetLendingRulesAsync));
        return Task.FromResult(Rules);
    }

    public Task<StaffMemberStatus> GetStatusAsync(
        Guid staffMemberId,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "Issuing a loan should not call GetStatusAsync.");

    public Task SetStatusAsync(
        Guid staffMemberId,
        StaffMemberStatus status,
        string reason,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "Issuing a loan should not change the staff status.");
    
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> staffMemberIds,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "Issuing a loan should not read staff names.");
    
    public Task<StaffMemberLookup?> FindAsync(
        string employeeIdOrEmail, 
        CancellationToken ct = default) 
        => throw new NotSupportedException(
            "Issuing a loan should not find staff members.");
    
    public Task<StaffContact?> GetContactAsync(
        Guid staffMemberId, 
        CancellationToken ct = default) 
        => throw new NotSupportedException(
            "Issuing a loan should not read staff member contact.");
    
    public Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(
        IReadOnlyCollection<Guid> staffMemberIds, 
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "Issuing a loan should not read staff member contact.");
}