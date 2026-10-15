using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

/// <summary>Login account plus its StaffMember (null if the account has none; AuthService then rejects the login).</summary>
public sealed record LoginAccount(
    Guid UserId,
    string Email,
    Guid? StaffMemberId,
    StaffMemberStatus? StaffStatus,
    IReadOnlyList<string> Roles,
    bool MustChangePassword = false);

/// <summary>Hides ASP.NET Core Identity from the Application layer.</summary>
public interface IUserAccountStore
{
    /// <summary>
    /// The account when the email exists, the account is not locked out and the password is right; otherwise null.
    /// Counts failed attempts (lockout). Every path checks one password hash, so the answer time does not reveal
    /// whether the email exists.
    /// </summary>
    Task<LoginAccount?> VerifyCredentialsAsync(string email, string password, CancellationToken ct);

    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
}
