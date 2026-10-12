using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Services;

public interface IAuthService
{
    /// <summary>
    /// MEM-02 login. Returns null for an unknown email or a wrong password (HTTP 401, which is not an ErrorKind).
    /// Throws <c>staff.email_not_corporate</c> (400) and <c>staff.account_deactivated</c> (403).
    /// </summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<bool> ChangeInitialPasswordAsync(Guid userId, ChangeInitialPasswordRequest request, CancellationToken ct = default);
}
