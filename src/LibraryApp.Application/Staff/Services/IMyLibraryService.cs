using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

public interface IMyLibraryService
{
    /// <summary>MEM-08 for the caller (ICurrentUser.StaffMemberId). Throws staff.not_found without a staff profile.</summary>
    Task<MyLibraryDto> GetAsync(CancellationToken ct = default);

    /// <summary>Updates only the caller's first and last name in one transaction.</summary>
    Task<MyProfileDto> UpdateProfileAsync(UpdateMyProfileRequest request, CancellationToken ct = default);
}
