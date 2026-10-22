using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Notifications;

public sealed class StaffContactRecipientReaderTests
{
    private static readonly Guid Anna = Guid.NewGuid();

    [Fact]
    public async Task Maps_the_contact_of_the_staff_member_to_the_recipient()
    {
        var staff = new FakeStaff(new StaffContact(Anna, "Anna Petrosyan", "anna@ameriabank.am", "manager@ameriabank.am", StaffMemberStatus.Active));

        var recipient = await new StaffContactRecipientReader(staff).FindAsync(Anna);

        Assert.NotNull(recipient);
        Assert.Equal(Anna, recipient.StaffMemberId);
        Assert.Equal("Anna Petrosyan", recipient.FullName);
        Assert.Equal("anna@ameriabank.am", recipient.Email);
        Assert.Equal("manager@ameriabank.am", recipient.LineManagerEmail);
    }

    [Fact]
    public async Task A_member_without_a_line_manager_has_none()
    {
        var staff = new FakeStaff(new StaffContact(Anna, "Anna Petrosyan", "anna@ameriabank.am", null, StaffMemberStatus.Active));

        Assert.Null((await new StaffContactRecipientReader(staff).FindAsync(Anna))!.LineManagerEmail);
    }

    [Fact]
    public async Task An_unknown_member_is_null_without_an_exception()
    {
        var staff = new FakeStaff(new StaffContact(Anna, "Anna Petrosyan", "anna@ameriabank.am", null, StaffMemberStatus.Active));

        Assert.Null(await new StaffContactRecipientReader(staff).FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_deactivated_member_is_still_found_because_a_notice_can_arrive_after_the_deactivation()
    {
        var staff = new FakeStaff(new StaffContact(Anna, "Anna Petrosyan", "anna@ameriabank.am", null, StaffMemberStatus.Deactivated));

        Assert.NotNull(await new StaffContactRecipientReader(staff).FindAsync(Anna));
    }

    [Fact]
    public async Task Asks_the_staff_contract_once_for_exactly_this_member_and_passes_the_token_on()
    {
        var staff = new FakeStaff(new StaffContact(Anna, "Anna Petrosyan", "anna@ameriabank.am", null, StaffMemberStatus.Active));
        using var cancellation = new CancellationTokenSource();

        await new StaffContactRecipientReader(staff).FindAsync(Anna, cancellation.Token);

        Assert.Equal([[Anna]], staff.Requests);
        Assert.Equal(cancellation.Token, staff.LastToken);
    }

    [Fact]
    public async Task A_cancelled_token_is_not_swallowed()
    {
        var staff = new FakeStaff();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StaffContactRecipientReader(staff).FindAsync(Anna, cancellation.Token));
    }

    private sealed class FakeStaff(params StaffContact[] contacts) : IStaffMemberService
    {
        public List<Guid[]> Requests { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(staffMemberIds.ToArray());
            LastToken = ct;
            return Task.FromResult<IReadOnlyDictionary<Guid, StaffContact>>(
                contacts.Where(c => staffMemberIds.Contains(c.StaffMemberId)).ToDictionary(c => c.StaffMemberId));
        }

        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
