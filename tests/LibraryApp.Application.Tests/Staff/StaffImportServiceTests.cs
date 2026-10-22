using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Exceptions;
using LibraryApp.Application.Staff.Options;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Staff.Validators;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Staff;
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
            new CorporateEmailPolicy(Options.Create(new CorporateEmailOptions())), repository, new FakeAccounts(), caller,
            Options.Create(new StaffOptions { Import = new() { MaxRows = 200, MaxFileBytes = 1_000_000 } }));

        var error = await Assert.ThrowsAsync<StaffImportException>(() => service.ImportAsync(Stream.Null));

        Assert.Contains(error.Errors, item => item.RowNumber == 3 && item.Field == nameof(StaffImportCsvRow.EmployeeId));
        Assert.Contains(error.Errors, item => item.RowNumber == 3 && item.Field == nameof(StaffImportCsvRow.CorporateEmail));
        Assert.Equal(0, management.BatchCalls);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task The_service_enforces_the_configured_row_limit_before_enrolling(int rowCount, bool exceedsLimit)
    {
        var rows = Enumerable.Range(0, rowCount).Select(i => Row(i + 2, $"EMP-{i}", $"user{i}@ameriabank.am")).ToArray();
        var management = new FakeManagementService();
        var service = new StaffImportService(new FakeParser(rows), management, new EnrolStaffMemberRequestValidator(),
            new CorporateEmailPolicy(Options.Create(new CorporateEmailOptions())), new FakeRepository(),
            new FakeAccounts(), new FakeCurrentUser(),
            Options.Create(new StaffOptions { Import = new() { MaxRows = 2, MaxFileBytes = 4096 } }));

        if (exceedsLimit)
        {
            var error = await Assert.ThrowsAsync<StaffCsvFormatException>(() => service.ImportAsync(Stream.Null));
            Assert.Equal(4, error.RowNumber);
            Assert.Contains("at most 2 data rows", error.Message);
            Assert.Equal(0, management.BatchCalls);
        }
        else
        {
            await service.ImportAsync(Stream.Null);
            Assert.Equal(1, management.BatchCalls);
        }
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
        public Task<StaffMemberProfile?> GetForProfileUpdateAsync(Guid id, CancellationToken ct) => Task.FromResult<StaffMemberProfile?>(null);
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
