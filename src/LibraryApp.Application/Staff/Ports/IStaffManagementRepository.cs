using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Staff.Ports;

/// <summary>Persistence needed by Staff's own management use cases; not an integration contract.</summary>
public interface IStaffManagementRepository
{
    Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<StaffMemberDto?> GetDetailsAsync(Guid id, CancellationToken ct);
    Task<bool> EmployeeIdExistsAsync(string employeeId, CancellationToken ct);
    Task<bool> EmailExistsAsync(string email, Guid? exceptMemberId, CancellationToken ct);
    Task<string?> GetDepartmentNameAsync(Guid id, CancellationToken ct);
    Task<string?> GetCategoryNameAsync(Guid id, CancellationToken ct);
    Task<PagedResult<StaffMemberListItemDto>> SearchAsync(StaffMemberSearchFilter filter, PageRequest paging, CancellationToken ct);
    void Add(StaffMember member);
}
