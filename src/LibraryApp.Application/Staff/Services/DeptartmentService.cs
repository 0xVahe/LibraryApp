using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

public class DepartmentService(IDepartmentRepository departments, IUnitOfWork unitOfWork, IIdGenerator idGenerator)
    : IDepartmentService
{
    public Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default) => departments.ListAsync(ct);

    public async Task<DepartmentDto> GetAsync(Guid id, CancellationToken ct = default) =>
        ToDto(await FindAsync(id, ct));

    public Task<DepartmentDto> CreateAsync(UpsertDepartmentRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var name = request.Name!.Trim();
            await RequireUniqueNameAsync(name, null, token);
            var department = new Department(idGenerator.NewId(), name);
            departments.Add(department);
            return ToDto(department);
        }, ct);

    public Task<DepartmentDto> UpdateAsync(Guid id, UpsertDepartmentRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var department = await FindAsync(id, token);
            var name = request.Name!.Trim();
            await RequireUniqueNameAsync(name, id, token);
            department.Rename(name);
            return ToDto(department);
        }, ct);

    // ---------------------------------------- Private helpers ---------------------------------------------
    private async Task RequireUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        if (await departments.NameExistsAsync(name, exceptId, ct))
            throw new DomainException(StaffErrors.DepartmentNameTaken);
    }

    private static DepartmentDto ToDto(Department department) => new(department.Id, department.Name);

    private async Task<Department> FindAsync(Guid id, CancellationToken ct) =>
        await departments.GetByIdAsync(id, ct) ?? throw new DomainException(StaffErrors.DepartmentNotFound);
}