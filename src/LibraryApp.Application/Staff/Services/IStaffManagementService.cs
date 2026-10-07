using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

/// <summary>Staff's own management use cases, separate from the frozen Borrowing integration contract.</summary>
public interface IStaffManagementService
{
    Task<StaffMemberDto> EnrolAsync(EnrolStaffMemberRequest request, bool callerIsAdministrator, CancellationToken ct = default);
    Task<StaffMemberDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<StaffMemberDto> UpdateAsync(Guid id, UpdateStaffMemberRequest request, CancellationToken ct = default);
}
