using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

/// <summary>The department list (MEM-06). No delete: staff members reference departments.</summary>
public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct = default);
    Task<DepartmentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DepartmentDto> CreateAsync(UpsertDepartmentRequest request, CancellationToken ct = default);
    Task<DepartmentDto> UpdateAsync(Guid id, UpsertDepartmentRequest request, CancellationToken ct = default);
}