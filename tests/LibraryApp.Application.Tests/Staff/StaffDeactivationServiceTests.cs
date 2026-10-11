using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryApp.Application.Tests.Staff;

public class StaffDeactivationServiceTests
{
    private readonly ManagementFake _store = new();
    private readonly LoanQueries _loans = new();
    private StaffDeactivationService Service => new(_store, _store, _loans, new CatalogServiceStub(),
        new StaffLock(_store, _loans), _store, _store, _store, _store, NullLogger<StaffDeactivationService>.Instance);

    private StaffMember Member(StaffMemberStatus status = StaffMemberStatus.Active)
    {
        var member = new StaffMember(Guid.NewGuid(), Guid.NewGuid(), "EMP-DEMO-01", "Demo", "Employee",
            "demo@ameriabank.am", _store.DepartmentId, "Engineer", null, _store.CategoryId, _store.UtcNow);
        if (status != StaffMemberStatus.Active) member.ChangeStatus(status);
        _store.Members.Add(member.Id, member);
        _store.Accounts.Add(member.UserId, (member.CorporateEmail, StaffRoles.StaffMember));
        return member;
    }

    [Theory]
    [InlineData(StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    public async Task Success_deactivates_account_and_publishes_one_event_in_the_same_transaction(StaffMemberStatus status)
    {
        var member = Member(status);
        await Service.DeactivateAsync(member.Id, new("  Left the bank  "));
        Assert.Equal(StaffMemberStatus.Deactivated, member.Status);
        Assert.Equal(member.UserId, Assert.Single(_store.DeactivatedAccounts));
        var changed = Assert.IsType<StaffStatusChanged>(Assert.Single(_store.Events));
        Assert.Equal(member.Id, changed.StaffMemberId);
        Assert.Equal(status, changed.OldStatus);
        Assert.Equal(StaffMemberStatus.Deactivated, changed.NewStatus);
        Assert.Equal("Left the bank", changed.Reason);
        Assert.Equal(_store.UtcNow, changed.OccurredAtUtc);
        Assert.NotEqual(Guid.Empty, changed.EventId);
        Assert.Null(changed.EffectiveUntilUtc);
        Assert.Equal(1, _store.TransactionCalls);
    }

    [Theory]
    [InlineData(LoanStatus.Active)]
    [InlineData(LoanStatus.Overdue)]
    public async Task Open_loans_return_titles_and_barcodes_without_writes(LoanStatus status)
    {
        var member = Member();
        _loans.Items = [new(Guid.NewGuid(), member.Id, CatalogServiceStub.OnLoanCopyId,
            CatalogServiceStub.AvailableBookId, _store.UtcNow, status, _store.UtcNow.AddDays(-21), 0)];
        var exception = await Assert.ThrowsAsync<DomainException>(() => Service.DeactivateAsync(member.Id, new("Left")));
        Assert.Equal(StaffErrors.HasActiveLoans, exception.Error);
        var books = Assert.IsAssignableFrom<IReadOnlyList<OutstandingBookDto>>(exception.Extensions["outstandingBooks"]);
        Assert.Equal(new("Risk Management Basics", "BK-0001-03"), Assert.Single(books));
        Assert.Equal(StaffMemberStatus.Active, member.Status);
        Assert.Empty(_store.DeactivatedAccounts);
        Assert.Empty(_store.Events);
    }

    [Fact]
    public async Task Missing_member_is_not_found_without_writes()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.DeactivateAsync(Guid.NewGuid(), new("Left")));
        Assert.Equal(StaffErrors.NotFound, error.Error);
        Assert.Empty(_store.DeactivatedAccounts);
        Assert.Empty(_store.Events);
    }

    [Fact]
    public async Task Repeated_deactivation_preserves_the_existing_domain_error_and_publishes_nothing()
    {
        var member = Member();
        member.Deactivate();
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.DeactivateAsync(member.Id, new("Left")));
        Assert.Equal(StaffErrors.AlreadyDeactivated, error.Error);
        Assert.Empty(_store.DeactivatedAccounts);
        Assert.Empty(_store.Events);
    }

    private sealed class LoanQueries : IBorrowingReadService
    {
        public bool LockAcquired { get; set; }
        public IReadOnlyList<OpenLoanInfo> Items { get; set; } = [];
        public Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default)
        {
            Assert.True(LockAcquired);
            return Task.FromResult(Items);
        }

        public Task<StaffBorrowingSummary> GetStaffSummaryAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException("Deactivation reads only the active loans.");

        public Task<IReadOnlyList<OpenLoanInfo>> GetLoansDueOnAsync(DateOnly localDate, CancellationToken ct = default) =>
            throw new NotSupportedException("Deactivation reads only the active loans.");
    }

    private sealed class StaffLock(ManagementFake store, LoanQueries loans) : IApplicationLock
    {
        public Task AcquireAsync(string resource, CancellationToken ct = default)
        {
            Assert.True(store.InTransaction);
            Assert.StartsWith("borrowing:staff:", resource);
            loans.LockAcquired = true;
            return Task.CompletedTask;
        }
    }
}
