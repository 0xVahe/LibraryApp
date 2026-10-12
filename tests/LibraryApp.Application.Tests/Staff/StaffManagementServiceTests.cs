using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Messaging;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Staff.Validators;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Options;
using LibraryApp.Application.Common;
using LibraryApp.Application.Tests.Fakes;

namespace LibraryApp.Application.Tests.Staff;

public class StaffManagementServiceTests
{
    private readonly ManagementFake _fake = new();
    private readonly FakeCurrentUser _caller = new();

    private StaffManagementService Service => new(_fake, _fake, _fake, _fake, _fake, _fake,
        new CorporateEmailPolicy(Options.Create(new CorporateEmailOptions())), _caller);

    private EnrolStaffMemberRequest Request => new("EMP-100", "Demo", "Employee", "demo@ameriabank.am",
        _fake.DepartmentId, "Engineer", null, _fake.CategoryId, "DemoPass1");

    [Fact]
    public async Task Enrol_defaults_role_and_publishes_the_new_member_id_inside_transaction()
    {
        var dto = await Service.EnrolAsync(Request);
        Assert.Equal(StaffRoles.StaffMember, dto.Role);
        Assert.Equal(_fake.UtcNow, dto.JoinedLibraryAtUtc);
        var enrolled = Assert.IsType<StaffEnrolled>(Assert.Single(_fake.Events));
        Assert.Equal(dto.Id, enrolled.StaffMemberId);
        Assert.NotEqual(Guid.Empty, enrolled.EventId);
        Assert.Equal(_fake.UtcNow, enrolled.OccurredAtUtc);
        Assert.Single(_fake.Accounts);
        Assert.False(_fake.InTransaction);
    }

    [Fact]
    public async Task Search_normalizes_filters_preserves_paging_and_returns_repository_result()
    {
        var departmentId = Guid.NewGuid();
        var paging = new PageRequest(2, 20);
        var expected = new PagedResult<StaffMemberListItemDto>(
            [
                new StaffMemberListItemDto(
                    Guid.NewGuid(), "EMP-100", "Aram", "Petrosyan",
                    "aram@ameriabank.am", departmentId, "IT", "Active")
            ],
            21, 2, 20);

        _fake.SearchResult = expected;

        var result =
            await Service.SearchAsync(new StaffMemberSearchRequest("  Aram  ", departmentId, "  active  ", paging));

        Assert.Equal(
            new StaffMemberSearchFilter("Aram", departmentId, StaffMemberStatus.Active),
            _fake.LastSearchFilter);
        Assert.Same(paging, _fake.LastSearchPaging);
        Assert.Same(expected, result);
        Assert.Equal(0, _fake.TransactionCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_treats_blank_filters_as_absent(string? value)
    {
        await Service.SearchAsync(new StaffMemberSearchRequest(value, null, value, new PageRequest()));

        Assert.Equal(new StaffMemberSearchFilter(null, null, null), _fake.LastSearchFilter);
    }

    [Theory]
    [InlineData("Active", StaffMemberStatus.Active)]
    [InlineData("Restricted", StaffMemberStatus.Restricted)]
    [InlineData("Suspended", StaffMemberStatus.Suspended)]
    [InlineData("Deactivated", StaffMemberStatus.Deactivated)]
    public async Task Search_accepts_each_named_status(string input, StaffMemberStatus expected)
    {
        await Service.SearchAsync(new StaffMemberSearchRequest(null, null, input, new PageRequest()));
        Assert.Equal(expected, _fake.LastSearchFilter!.Status);
    }

    [Theory]
    [InlineData("Banana")]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData("Active, Restricted")]
    public async Task Search_rejects_invalid_status_before_calling_repository(string status)
    {
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.SearchAsync(new StaffMemberSearchRequest(null, null, status, new PageRequest())));

        Assert.Equal(StaffErrors.StatusInvalid, error.Error);
        Assert.Null(_fake.LastSearchFilter);
    }

    [Fact]
    public async Task Corporate_email_policy_is_configurable_for_enrolment()
    {
        var service = new StaffManagementService(_fake, _fake, _fake, _fake, _fake, _fake,
            new CorporateEmailPolicy(Options.Create(new CorporateEmailOptions { Domain = "example.org" })), _caller);
        Assert.Equal("demo@example.org",
            (await service.EnrolAsync(Request with { CorporateEmail = "demo@example.org" })).CorporateEmail);
    }

    [Fact]
    public async Task Noncorporate_email_has_exact_error_and_never_opens_a_transaction()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.EnrolAsync(Request with { CorporateEmail = "demo@gmail.com" }));
        Assert.Equal("Only Ameriabank staff can be enrolled", error.Error.Message);
        Assert.Equal(ErrorKind.Validation, error.Error.Kind);
        Assert.Equal(0, _fake.TransactionCalls);
    }

    [Theory]
    [InlineData(StaffRoles.LibraryAdministrator)]
    [InlineData(StaffRoles.Librarian)]
    public async Task Librarian_cannot_assign_privileged_roles_before_any_write(string role)
    {
        _caller.Role = StaffRoles.Librarian;
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.EnrolAsync(Request with { Role = role }));
        Assert.Equal(StaffErrors.RoleAssignmentForbidden, error.Error);
        Assert.Empty(_fake.Accounts);
        Assert.Equal(0, _fake.TransactionCalls);
    }

    [Theory]
    [InlineData(StaffRoles.LibraryAdministrator)]
    [InlineData(StaffRoles.Librarian)]
    public async Task Administrator_can_assign_privileged_roles(string role)
    {
        _caller.Role = StaffRoles.LibraryAdministrator;
        var dto = await Service.EnrolAsync(Request with { Role = role });
        Assert.Equal(role, dto.Role);
        Assert.Equal(role, Assert.Single(_fake.Accounts).Value.Role);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_checks_run_before_creating_an_account(bool employeeId)
    {
        await Service.EnrolAsync(Request);
        var request = employeeId
            ? Request with { CorporateEmail = "other@ameriabank.am" }
            : Request with { EmployeeId = "EMP-200" };
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.EnrolAsync(request));
        Assert.Equal(employeeId ? StaffErrors.EmployeeIdTaken : StaffErrors.EmailTaken, error.Error);
        Assert.Single(_fake.Accounts);
        Assert.Single(_fake.Members);
        Assert.Single(_fake.Events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_reference_is_a_validation_error_before_creating_account(bool department)
    {
        var request = department
            ? Request with { DepartmentId = Guid.NewGuid() }
            : Request with { StaffCategoryId = Guid.NewGuid() };
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.EnrolAsync(request));
        Assert.Equal(department ? StaffErrors.DepartmentInvalid : StaffErrors.CategoryNotFound, error.Error);
        Assert.Empty(_fake.Accounts);
    }

    [Fact]
    public async Task Identity_failure_prevents_member_and_event_creation()
    {
        _fake.FailCreate = true;
        await Assert.ThrowsAsync<DomainException>(() => Service.EnrolAsync(Request));
        Assert.Empty(_fake.Members);
        Assert.Empty(_fake.Events);
    }

    [Fact]
    public async Task Update_keeps_id_employee_id_role_status_and_joined_date_and_updates_account_email()
    {
        var before = await Service.EnrolAsync(Request);
        var request = new UpdateStaffMemberRequest("Updated", "Employee", "new@ameriabank.am", _fake.DepartmentId,
            "Senior Engineer", "manager@example.org", _fake.CategoryId);
        var after = await Service.UpdateAsync(before.Id, request);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.EmployeeId, after.EmployeeId);
        Assert.Equal(before.JoinedLibraryAtUtc, after.JoinedLibraryAtUtc);
        Assert.Equal(before.Role, after.Role);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal("new@ameriabank.am", Assert.Single(_fake.Accounts).Value.Email);
        Assert.Equal(request.FirstName, after.FirstName);
        Assert.Equal(request.Position, after.Position);
        Assert.Single(_fake.Events);
    }

    [Fact]
    public async Task Update_deactivated_member_is_a_conflict_and_does_not_update_account()
    {
        var dto = await Service.EnrolAsync(Request);
        _fake.Members[dto.Id].Deactivate();
        var error = await Assert.ThrowsAsync<DomainException>(() => Service.UpdateAsync(dto.Id,
            new("Demo", "Employee", "new@ameriabank.am", _fake.DepartmentId, "Engineer", null, _fake.CategoryId)));
        Assert.Equal("staff.deactivated_profile_locked", error.Error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Error.Kind);
        Assert.Equal(Request.CorporateEmail, Assert.Single(_fake.Accounts).Value.Email);
    }

    [Theory]
    [InlineData("Short1")]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    [InlineData("abcdefgh١")]
    public async Task Validator_identifies_invalid_password_field(string password)
    {
        var result = await new EnrolStaffMemberRequestValidator().ValidateAsync(Request with { Password = password });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(EnrolStaffMemberRequest.Password));
    }

    [Fact]
    public async Task Missing_member_is_not_found() =>
        Assert.Equal(StaffErrors.NotFound,
            (await Assert.ThrowsAsync<DomainException>(() => Service.GetAsync(Guid.NewGuid()))).Error);
}

/// <summary>Records use-case writes and enforces the transaction boundary. Real rollback is tested through the API on SQL Server.</summary>
internal sealed class ManagementFake : IStaffManagementRepository, IStaffAccountStore, IUnitOfWork, IEventBus, IClock,
    IIdGenerator
{
    public Guid DepartmentId { get; } = Guid.NewGuid();
    public Guid CategoryId { get; } = Guid.NewGuid();
    public Dictionary<Guid, StaffMember> Members { get; } = [];
    public Dictionary<Guid, (string Email, string Role)> Accounts { get; } = [];
    public List<IDomainEvent> Events { get; } = [];
    public bool InTransaction { get; private set; }
    public int TransactionCalls { get; private set; }
    public bool FailCreate { get; set; }
    public List<Guid> DeactivatedAccounts { get; } = [];
    public StaffMemberSearchFilter? LastSearchFilter { get; private set; }
    public PageRequest? LastSearchPaging { get; private set; }
    public PagedResult<StaffMemberListItemDto>? SearchResult { get; set; }

    public Task<PagedResult<StaffMemberListItemDto>> SearchAsync(
        StaffMemberSearchFilter filter,
        PageRequest paging,
        CancellationToken ct)
    {
        LastSearchFilter = filter;
        LastSearchPaging = paging;

        return Task.FromResult(SearchResult ?? PagedResult<StaffMemberListItemDto>.Empty(paging));
    }

    public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Members.GetValueOrDefault(id));

    public Task<StaffMemberDto?> GetDetailsAsync(Guid id, CancellationToken ct) => Task.FromResult(
        Members.TryGetValue(id, out var m)
            ? new StaffMemberDto(m.Id, m.EmployeeId, m.FirstName, m.LastName, m.CorporateEmail, m.DepartmentId, "IT",
                m.Position,
                m.LineManagerEmail, m.StaffCategoryId, "Employee", m.Status.ToString(), m.JoinedLibraryAtUtc,
                Accounts[m.UserId].Role)
            : null);

    public Task<bool> EmployeeIdExistsAsync(string employeeId, CancellationToken ct) =>
        Task.FromResult(Members.Values.Any(x => x.EmployeeId == employeeId));

    Task<bool> IStaffManagementRepository.EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct) =>
        Task.FromResult(Members.Values.Any(x => x.CorporateEmail == email && x.Id != exceptId));

    Task<bool> IStaffAccountStore.EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct) =>
        Task.FromResult(Accounts.Any(x => x.Value.Email == email && x.Key != exceptId));

    Task<IReadOnlyCollection<string>> IStaffAccountStore.GetExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>(Accounts.Values.Where(account => emails.Contains(account.Email, StringComparer.OrdinalIgnoreCase))
            .Select(account => account.Email).ToArray());

    public Task<string?> GetDepartmentNameAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(id == DepartmentId ? "IT" : null);

    public Task<string?> GetCategoryNameAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(id == CategoryId ? "Employee" : null);

    public Task<IReadOnlyList<Department>> GetDepartmentsByNamesAsync(IReadOnlyCollection<string> names, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Department>>(names.Contains("IT", StringComparer.OrdinalIgnoreCase)
            ? [new Department(DepartmentId, "IT")] : []);

    public Task<IReadOnlyList<StaffCategory>> GetCategoriesByNamesAsync(IReadOnlyCollection<string> names, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StaffCategory>>(names.Contains("Employee", StringComparer.OrdinalIgnoreCase)
            ? [new StaffCategory(CategoryId, "Employee", 5, 14, 1)] : []);

    public Task<IReadOnlyCollection<string>> GetExistingEmployeeIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>(Members.Values.Where(member => ids.Contains(member.EmployeeId, StringComparer.OrdinalIgnoreCase))
            .Select(member => member.EmployeeId).ToArray());

    Task<IReadOnlyCollection<string>> IStaffManagementRepository.GetExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>(Members.Values.Where(member => emails.Contains(member.CorporateEmail, StringComparer.OrdinalIgnoreCase))
            .Select(member => member.CorporateEmail).ToArray());

    Task<IReadOnlyDictionary<Guid, string>> IStaffManagementRepository.GetDepartmentNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.Contains(DepartmentId) ? new Dictionary<Guid, string> { [DepartmentId] = "IT" } : new Dictionary<Guid, string>());

    Task<IReadOnlyDictionary<Guid, string>> IStaffManagementRepository.GetCategoryNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.Contains(CategoryId) ? new Dictionary<Guid, string> { [CategoryId] = "Employee" } : new Dictionary<Guid, string>());

    public void Add(StaffMember member)
    {
        Assert.True(InTransaction);
        Members.Add(member.Id, member);
    }

    public Task CreateAsync(Guid userId, string email, string password, string role, CancellationToken ct,
        bool requirePasswordChange = false)
    {
        Assert.True(InTransaction);
        if (FailCreate) throw new DomainException(StaffErrors.AccountWriteFailed);
        Accounts.Add(userId, (email, role));
        return Task.CompletedTask;
    }

    public Task UpdateEmailAsync(Guid userId, string email, CancellationToken ct)
    {
        Assert.True(InTransaction);
        Accounts[userId] = (email, Accounts[userId].Role);
        return Task.CompletedTask;
    }

    public Task<string> GetRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult(Accounts[userId].Role);

    public Task DeactivateAsync(Guid userId, CancellationToken ct)
    {
        Assert.True(InTransaction);
        DeactivatedAccounts.Add(userId);
        return Task.CompletedTask;
    }

    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent
    {
        Assert.True(InTransaction);
        Events.Add(domainEvent);
        return Task.CompletedTask;
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        Assert.False(InTransaction);
        TransactionCalls++;
        InTransaction = true;
        try
        {
            return await operation(ct);
        }
        finally
        {
            InTransaction = false;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
        CancellationToken ct = default) =>
        await ExecuteInTransactionAsync(async token =>
        {
            await operation(token);
            return true;
        }, ct);

    public Guid NewId() => Guid.NewGuid();
    public DateTime UtcNow => new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    public DateOnly LocalToday => new(2026, 10, 7);
    public DateTime ToLocal(DateTime utc) => utc.AddHours(4);
    public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public DateTime StartOfLocalDayUtc(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-4);

    public DateTime EndOfLocalDayUtc(DateOnly date) => StartOfLocalDayUtc(date.AddDays(1)).AddTicks(-1);
}
