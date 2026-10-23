using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

/// <summary>
/// Department list (MEM-06). The name check gives a clear 409; when two requests pass it at the same time,
/// UX_Departments_Name rejects the second one and that is reported with the same error.
/// </summary>
public sealed class DepartmentService(IDepartmentRepository departments, IUnitOfWork unitOfWork, IIdGenerator idGenerator)
    : IDepartmentService
{
    public Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default) => departments.ListAsync(ct);

    public Task<IReadOnlyList<DepartmentSettingsDto>> ListForSettingsAsync(CancellationToken ct = default) =>
        departments.ListForSettingsAsync(ct);

    public async Task<DepartmentDto> GetAsync(Guid id, CancellationToken ct = default) =>
        ToDto(await FindAsync(id, ct));

    public Task<DepartmentDto> CreateAsync(UpsertDepartmentRequest request, CancellationToken ct = default) =>
        SaveAsync(async token =>
        {
            var name = request.Name!.Trim();
            await RequireUniqueNameAsync(name, null, token);
            var department = new Department(idGenerator.NewId(), name);
            departments.Add(department);
            return ToDto(department);
        }, ct);

    public Task<DepartmentDto> UpdateAsync(Guid id, UpsertDepartmentRequest request, CancellationToken ct = default) =>
        SaveAsync(async token =>
        {
            var department = await FindAsync(id, token);
            var name = request.Name!.Trim();
            await RequireUniqueNameAsync(name, id, token);
            department.Rename(name);
            return ToDto(department);
        }, ct);

    // ---------------------------------------- Private helpers ---------------------------------------------
    /// <summary>The name index is the only unique index a department write can break.</summary>
    private async Task<DepartmentDto> SaveAsync(Func<CancellationToken, Task<DepartmentDto>> operation, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(operation, ct);
        }
        catch (DomainException ex) when (ex.Error == CommonErrors.UniqueViolation)
        {
            throw new DomainException(StaffErrors.DepartmentNameTaken, ex);
        }
    }

    private async Task RequireUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        if (await departments.NameExistsAsync(name, exceptId, ct))
            throw new DomainException(StaffErrors.DepartmentNameTaken);
    }

    private static DepartmentDto ToDto(Department department) => new(department.Id, department.Name);

    private async Task<Department> FindAsync(Guid id, CancellationToken ct) =>
        await departments.GetByIdAsync(id, ct) ?? throw new DomainException(StaffErrors.DepartmentNotFound);
}
