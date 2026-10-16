using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

/// <summary>MEM-02: the signed-in staff member changes their own password.</summary>
public interface IPasswordChangeService
{
    /// <summary>Throws <c>staff.password_change_failed</c> (400) for a wrong current password, a locked account
    /// or a new password the rules reject.</summary>
    Task ChangeAsync(ChangePasswordRequest request, CancellationToken ct = default);
}