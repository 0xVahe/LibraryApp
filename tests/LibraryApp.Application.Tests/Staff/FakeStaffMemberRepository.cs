using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Staff;

internal sealed class FakeStaffMemberRepository : IStaffMemberRepository
{
    private readonly Dictionary<Guid, StaffMember> _members = new();
    private readonly Dictionary<Guid, StaffCategory> _categories = new();

    public static readonly DateTime JoinedAt = new(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);

    public StaffMember Add(StaffMemberStatus status = StaffMemberStatus.Active, int maxLoans = 3, int loanDays = 21, int maxRenewals = 1, string firstName = "Anna") 
    {
        var category = new StaffCategory(Guid.NewGuid(), $"Category {_categories.Count + 1}", maxLoans, loanDays, maxRenewals);
        _categories[category.Id] = category;

        var member = new StaffMember(
            Guid.NewGuid(), Guid.NewGuid(), $"EMP-{_members.Count + 1:0000}", firstName, "Petrosyan",
            $"anna{_members.Count + 1}@ameriabank.am", Guid.NewGuid(), "Analyst", null, category.Id, JoinedAt);

        if (status == StaffMemberStatus.Deactivated)
            member.Deactivate();
        else if (status != StaffMemberStatus.Active)
            member.ChangeStatus(status);

        _members[member.Id] = member;
        return member;
    }

    public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_members.GetValueOrDefault(id));

    public Task<StaffMemberStatus?> GetStatusAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_members.TryGetValue(id, out var member) ? member.Status : (StaffMemberStatus?)null);

    public Task<StaffLendingInfo?> GetLendingInfoAsync(Guid id, CancellationToken ct)
    {
        if (!_members.TryGetValue(id, out var member))
            return Task.FromResult<StaffLendingInfo?>(null);

        var category = _categories[member.StaffCategoryId];
        return Task.FromResult<StaffLendingInfo?>(new StaffLendingInfo(
            member.Status, new LendingRules(category.MaxLoans, category.LoanDays, category.MaxRenewals)));
    }
    
    public int GetNamesCalls { get; private set; }

    // ToDictionary throws on a repeated id, so a test fails if the service stops removing duplicates.
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        GetNamesCalls++;
        IReadOnlyDictionary<Guid, string> names = ids
            .Where(_members.ContainsKey)
            .ToDictionary(id => id, id => $"{_members[id].FirstName} {_members[id].LastName}");
        return Task.FromResult(names);
    }
}