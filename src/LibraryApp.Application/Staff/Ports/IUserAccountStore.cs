using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

/// <summary>Login account plus its StaffMember (null if the account has none).</summary>
public sealed record LoginAccount(
    Guid UserId,
    string Email,
    Guid? StaffMemberId,
    StaffMemberStatus? StaffStatus,
    IReadOnlyList<string> Roles);

/// <summary>Hides ASP.NET Core Identity from the Application layer.</summary>
public interface IUserAccountStore
{
    Task<LoginAccount?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>Counts failed attempts; a locked-out account always returns false.</summary>
    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken ct);
}