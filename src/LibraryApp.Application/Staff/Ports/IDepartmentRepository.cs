using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

public interface IDepartmentRepository
{
    Task<IReadOnlyList<DepartmentDto>> ListAsync(CancellationToken ct);
    Task<Department?> GetByIdAsync(Guid id, CancellationToken ct);
    /// <summary>Compares like UX_Departments_Name does (database collation, case-insensitive).</summary>
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct);
    void Add(Department department);
}