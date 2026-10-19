using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

public interface IMyLibraryService
{
    /// <summary>Returns MEM-08 for the caller (ICurrentUser.StaffMemberId). Throws staff.not_found without a profile.</summary>
    Task<MyLibraryDto> GetAsync(CancellationToken ct = default);

    /// <summary>S2-01: updates only the caller's first and last name in one transaction.</summary>
    Task<MyProfileDto> UpdateProfileAsync(UpdateMyProfileRequest request, CancellationToken ct = default);
}
