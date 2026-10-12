using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Staff.Validators;
using LibraryApp.Domain.Staff;
using LibraryApp.Application.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Staff;

public sealed class StaffImportServiceTests
{
    [Fact]
    public async Task Duplicate_employee_id_and_email_in_file_return_row_field_errors_without_enrolling()
    {
        var rows = new[]
        {
            Row(2, "EMP-1", "same@ameriabank.am"),
            Row(3, "emp-1", "SAME@ameriabank.am")
        };
        var management = new FakeManagementService();
        var repository = new FakeRepository();
        var caller = new FakeCurrentUser();
        var service = new StaffImportService(new FakeParser(rows), management, new EnrolStaffMemberRequestValidator(),
            new CorporateEmailPolicy(Options.Create(new CorporateEmailOptions())), repository, new FakeAccounts(), caller);

        var error = await Assert.ThrowsAsync<StaffImportException>(() => service.ImportAsync(Stream.Null));

        Assert.Contains(error.Errors, item => item.RowNumber == 3 && item.Field == nameof(StaffImportCsvRow.EmployeeId));
        Assert.Contains(error.Errors, item => item.RowNumber == 3 && item.Field == nameof(StaffImportCsvRow.CorporateEmail));
        Assert.Equal(0, management.BatchCalls);
    }

    private static StaffImportCsvRow Row(int number, string employeeId, string email) =>
        new(number, employeeId, "Anna", "Employee", email, "IT", "Engineer", null, "Employee", null);

    private sealed class FakeParser(IReadOnlyList<StaffImportCsvRow> rows) : IStaffCsvParser
    {
        public Task<IReadOnlyList<StaffImportCsvRow>> ParseAsync(Stream csv, CancellationToken ct = default) =>
            Task.FromResult(rows);
    }

    private sealed class FakeManagementService : IStaffManagementService
    {
        public int BatchCalls { get; private set; }
        public Task<IReadOnlyList<StaffMemberDto>> EnrolBatchAsync(IReadOnlyList<EnrolStaffMemberRequest> requests,
            CancellationToken ct = default)
        {
            BatchCalls++;
            return Task.FromResult<IReadOnlyList<StaffMemberDto>>([]);
        }

        public Task<StaffMemberDto> EnrolAsync(EnrolStaffMemberRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffMemberDto> GetAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffMemberDto> UpdateAsync(Guid id, UpdateStaffMemberRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<StaffMemberListItemDto>> SearchAsync(StaffMemberSearchRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeRepository : IStaffManagementRepository
    {
        public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<StaffMember?>(null);
        public Task<StaffMemberDto?> GetDetailsAsync(Guid id, CancellationToken ct) => Task.FromResult<StaffMemberDto?>(null);
        public Task<bool> EmployeeIdExistsAsync(string employeeId, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> EmailExistsAsync(string email, Guid? exceptMemberId, CancellationToken ct) => Task.FromResult(false);
        public Task<string?> GetDepartmentNameAsync(Guid id, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<string?> GetCategoryNameAsync(Guid id, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<Department>> GetDepartmentsByNamesAsync(IReadOnlyCollection<string> names, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Department>>([new Department(Guid.Parse("10000000-0000-0000-0000-000000000001"), "IT")]);
        public Task<IReadOnlyList<StaffCategory>> GetCategoriesByNamesAsync(IReadOnlyCollection<string> names, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StaffCategory>>([new StaffCategory(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Employee", 5, 14, 1)]);
        public Task<IReadOnlyCollection<string>> GetExistingEmployeeIdsAsync(IReadOnlyCollection<string> employeeIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IReadOnlyCollection<string>> GetExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyDictionary<Guid, string>> GetCategoryNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<PagedResult<StaffMemberListItemDto>> SearchAsync(StaffMemberSearchFilter filter, PageRequest paging, CancellationToken ct) => throw new NotImplementedException();
        public void Add(StaffMember member) => throw new NotImplementedException();
    }

    private sealed class FakeAccounts : IStaffAccountStore
    {
        public Task<bool> EmailExistsAsync(string email, Guid? exceptUserId, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyCollection<string>> GetExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task CreateAsync(Guid userId, string email, string password, string role, CancellationToken ct,
            bool requirePasswordChange = false) => throw new NotImplementedException();
        public Task UpdateEmailAsync(Guid userId, string email, CancellationToken ct) => throw new NotImplementedException();
        public Task DeactivateAsync(Guid userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<string> GetRoleAsync(Guid userId, CancellationToken ct) => throw new NotImplementedException();
    }
}
