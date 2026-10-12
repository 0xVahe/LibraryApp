using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Staff.Services;

/// <summary>Staff's own management use cases, separate from the frozen Borrowing integration contract.</summary>
/// <remarks>Request DTOs are validated at the API boundary by ValidationFilter before calling these use cases.</remarks>
public interface IStaffManagementService
{
    Task<StaffMemberDto> EnrolAsync(EnrolStaffMemberRequest request, CancellationToken ct = default);
    Task<StaffMemberDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<StaffMemberDto> UpdateAsync(Guid id, UpdateStaffMemberRequest request, CancellationToken ct = default);
    Task<PagedResult<StaffMemberListItemDto>> SearchAsync(StaffMemberSearchRequest request, CancellationToken ct = default);
}
