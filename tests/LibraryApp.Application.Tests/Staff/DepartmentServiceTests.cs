using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Staff.Validators;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Staff;

public class DepartmentServiceTests
{
    private readonly DepartmentFake _fake = new();
    private DepartmentService Service => new(_fake, _fake, _fake);

    [Fact]
    public async Task Create_trims_the_name_and_adds_a_department_inside_a_transaction()
    {
        var dto = await Service.CreateAsync(new("     Test     "));

        Assert.Equal("Test", dto.Name);
        Assert.Equal(dto, await Service.GetAsync(dto.Id));
        Assert.False(_fake.InTransaction);
    }

    [Fact]
    public async Task Update_renames_the_department()
    {
        var dto = await Service.CreateAsync(new("Test"));

        var updated = await Service.UpdateAsync(dto.Id, new("Marketing"));

        Assert.Equal("Marketing", updated.Name);
        Assert.Equal(new DepartmentDto(dto.Id, "Marketing"), updated);
        Assert.Equal("Marketing", _fake.Departments[dto.Id].Name);
    }

    [Fact]
    public async Task Update_may_keep_its_own_name_in_another_case()
    {
        var dto = await Service.CreateAsync(new("Treasury"));

        Assert.Equal("TREASURY", (await Service.UpdateAsync(dto.Id, new("TREASURY"))).Name);
    }

    [Fact]
    public async Task Create_with_a_taken_name_throws_conflict_and_adds_nothing()
    {
        await Service.CreateAsync(new("Treasury"));

        var error = await Assert.ThrowsAsync<DomainException>(() => Service.CreateAsync(new(" treasury ")));

        Assert.Equal(StaffErrors.DepartmentNameTaken, error.Error);
        Assert.Equal(ErrorKind.Conflict, error.Error.Kind);
        Assert.Single(_fake.Departments);
    }

    [Fact]
    public async Task Update_to_a_taken_name_throws_conflict_and_keeps_the_old_name()
    {
        await Service.CreateAsync(new("Treasury"));
        var other = await Service.CreateAsync(new("Finance"));

        var error = await Assert.ThrowsAsync<DomainException>(() => Service.UpdateAsync(other.Id, new("Treasury")));

        Assert.Equal(StaffErrors.DepartmentNameTaken, error.Error);
        Assert.Equal("Finance", _fake.Departments[other.Id].Name);
    }

    [Fact]
    public async Task Unknown_id_is_not_found_for_get_and_update()
    {
        var id = Guid.NewGuid();

        var get = await Assert.ThrowsAsync<DomainException>(() => Service.GetAsync(id));
        var update = await Assert.ThrowsAsync<DomainException>(() => Service.UpdateAsync(id, new("Treasury")));

        Assert.Equal(StaffErrors.DepartmentNotFound, get.Error);
        Assert.Equal(StaffErrors.DepartmentNotFound, update.Error);
        Assert.Equal(ErrorKind.NotFound, get.Error.Kind);
        Assert.Empty(_fake.Departments);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("IT", true)]
    public void Validator_requires_a_name(string? name, bool valid) =>
        Assert.Equal(valid, new UpsertDepartmentRequestValidator().Validate(new UpsertDepartmentRequest(name)).IsValid);

    [Fact]
    public void Validator_counts_the_length_after_trimming()
    {
        var validator = new UpsertDepartmentRequestValidator();

        Assert.True(validator.Validate(new UpsertDepartmentRequest($"  {new string('a', 100)}  ")).IsValid);
        Assert.False(validator.Validate(new UpsertDepartmentRequest(new string('a', 101))).IsValid);
    }

    [Fact]
    public async Task Unique_index_violation_from_a_concurrent_write_is_reported_as_name_taken()
    {
        var existing = await Service.CreateAsync(new("Treasury"));
        _fake.FailSaveWithUniqueViolation = true;

        var create = await Assert.ThrowsAsync<DomainException>(() => Service.CreateAsync(new("Markets")));
        var update = await Assert.ThrowsAsync<DomainException>(() => Service.UpdateAsync(existing.Id, new("Markets")));

        Assert.Equal(StaffErrors.DepartmentNameTaken, create.Error);
        Assert.Equal(StaffErrors.DepartmentNameTaken, update.Error);
    }
}

internal sealed class DepartmentFake : IDepartmentRepository, IUnitOfWork, IIdGenerator
{
    public Dictionary<Guid, Department> Departments { get; } = [];
    public bool InTransaction { get; private set; }
    public bool FailSaveWithUniqueViolation { get; set; }

    public Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DepartmentDto>>(Departments.Values.Select(x => new DepartmentDto(x.Id, x.Name))
            .ToList());

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Departments.GetValueOrDefault(id));

    // Case-insensitive, like the SQL Server collation behind UX_Departments_Name.
    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct) => Task.FromResult(
        Departments.Values.Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) && x.Id != exceptId));

    public void Add(Department department)
    {
        Assert.True(InTransaction);
        Departments.Add(department.Id, department);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        Assert.False(InTransaction);
        InTransaction = true;
        try
        {
            var result = await operation(ct);
            if (FailSaveWithUniqueViolation)
                throw new DomainException(CommonErrors.UniqueViolation);
            return result;
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
}