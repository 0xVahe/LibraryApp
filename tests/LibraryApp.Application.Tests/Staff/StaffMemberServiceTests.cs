using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Staff;

public class StaffMemberServiceTests
{
    private readonly FakeStaffMemberRepository _repository = new();
    private readonly StaffMemberService _service;

    public StaffMemberServiceTests() => _service = new StaffMemberService(_repository);

    // ---- CanBorrowAsync (INT-03) ----

    [Theory]
    [InlineData(StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Restricted)]
    public async Task CanBorrow_allows_Active_and_Restricted(StaffMemberStatus status)
    {
        var member = _repository.Add(status);

        var result = await _service.CanBorrowAsync(member.Id);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CanBorrow_denies_a_Suspended_member()
    {
        var member = _repository.Add(StaffMemberStatus.Suspended);

        var result = await _service.CanBorrowAsync(member.Id);

        Assert.False(result.IsAllowed);
        Assert.Equal(StaffErrors.Suspended, result.Error);
    }

    [Fact]
    public async Task CanBorrow_denies_a_Deactivated_member()
    {
        var member = _repository.Add(StaffMemberStatus.Deactivated);

        var result = await _service.CanBorrowAsync(member.Id);

        Assert.Equal(StaffErrors.Deactivated, result.Error);
    }

    [Fact]
    public async Task CanBorrow_denies_an_unknown_member()
    {
        var result = await _service.CanBorrowAsync(Guid.NewGuid());

        Assert.Equal(StaffErrors.NotFound, result.Error);
    }

    // ---- GetLendingRulesAsync (INT-04) ----

    [Fact]
    public async Task GetLendingRules_returns_the_category_limits_for_an_Active_member()
    {
        var member = _repository.Add(StaffMemberStatus.Active, maxLoans: 5, loanDays: 30, maxRenewals: 2);

        var rules = await _service.GetLendingRulesAsync(member.Id);

        Assert.Equal(new LendingRules(5, 30, 2), rules);
    }

    [Fact]
    public async Task GetLendingRules_gives_a_Restricted_member_one_loan_and_no_renewals()
    {
        var member = _repository.Add(StaffMemberStatus.Restricted, maxLoans: 5, loanDays: 30, maxRenewals: 2);

        var rules = await _service.GetLendingRulesAsync(member.Id);

        Assert.Equal(new LendingRules(1, 30, 0), rules);
    }

    [Fact]
    public async Task GetLendingRules_throws_for_an_unknown_member()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.GetLendingRulesAsync(Guid.NewGuid()));

        Assert.Equal(StaffErrors.NotFound, ex.Error);
    }

    // ---- GetStatusAsync ----

    [Fact]
    public async Task GetStatus_returns_the_current_status()
    {
        var member = _repository.Add(StaffMemberStatus.Restricted);

        Assert.Equal(StaffMemberStatus.Restricted, await _service.GetStatusAsync(member.Id));
    }

    [Fact]
    public async Task GetStatus_throws_for_an_unknown_member()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.GetStatusAsync(Guid.NewGuid()));

        Assert.Equal(StaffErrors.NotFound, ex.Error);
    }

    // ---- SetStatusAsync (INT-05) ----

    [Fact]
    public async Task SetStatus_changes_the_status()
    {
        var member = _repository.Add();

        await _service.SetStatusAsync(member.Id, StaffMemberStatus.Restricted, "5 active penalty points");

        Assert.Equal(StaffMemberStatus.Restricted, member.Status);
    }

    [Fact]
    public async Task SetStatus_with_the_same_status_does_nothing()
    {
        var member = _repository.Add(StaffMemberStatus.Restricted);

        await _service.SetStatusAsync(member.Id, StaffMemberStatus.Restricted, "recalculation");

        Assert.Equal(StaffMemberStatus.Restricted, member.Status);
    }

    [Fact]
    public async Task SetStatus_does_not_touch_a_Deactivated_member()
    {
        var member = _repository.Add(StaffMemberStatus.Deactivated);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => _service.SetStatusAsync(member.Id, StaffMemberStatus.Active, "points expired"));

        Assert.Equal(StaffErrors.DeactivatedCannotChangeStatus, ex.Error);
        Assert.Equal(StaffMemberStatus.Deactivated, member.Status);
    }

    [Fact]
    public async Task SetStatus_cannot_deactivate()
    {
        var member = _repository.Add();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => _service.SetStatusAsync(member.Id, StaffMemberStatus.Deactivated, "left the bank"));

        Assert.Equal(StaffErrors.StatusInvalid, ex.Error);
    }

    [Fact]
    public async Task SetStatus_throws_for_an_unknown_member()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => _service.SetStatusAsync(Guid.NewGuid(), StaffMemberStatus.Restricted, "test"));

        Assert.Equal(StaffErrors.NotFound, ex.Error);
    }

    [Fact]
    public void LoanLimitReached_has_the_agreed_message()
    {
        var error = StaffErrors.LoanLimitReached(3, 3);

        Assert.Equal("staff.loan_limit_reached", error.Code);
        Assert.Equal("Loan limit reached (3/3).", error.Message);
    }
}