using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

/// <summary>Login account plus its StaffMember (null if the account has none; AuthService then rejects the login).</summary>
public sealed record LoginAccount(
    Guid UserId,
    string Email,
    Guid? StaffMemberId,
    StaffMemberStatus? StaffStatus,
    IReadOnlyList<string> Roles,
    string SecurityStamp,
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

    /// <summary>
    /// True when the account exists, is not locked out and the current password is right.
    /// A wrong password counts as a failed attempt (lockout), like a failed login.
    /// </summary>
    Task<bool> VerifyCurrentPasswordAsync(Guid userId, string currentPassword, CancellationToken ct);

    /// <summary>
    /// Sets the new password and clears failed attempts and the first-login flag. Opens no transaction: call it
    /// inside IUnitOfWork. False when the password rules reject the new password.
    /// </summary>
    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
}
