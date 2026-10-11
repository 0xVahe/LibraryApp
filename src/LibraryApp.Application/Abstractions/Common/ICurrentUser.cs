namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// The caller of the current request, read from the access token. Use it instead of parsing claims.
/// Available only while handling an authenticated HTTP request; event handlers, jobs and seeders must not use it.
/// Every member throws InvalidOperationException when there is no authenticated user.
/// </summary>
public interface ICurrentUser
{
    /// <summary>AppUser.Id (the "sub" claim).</summary>
    Guid UserId { get; }

    /// <summary>StaffMember.Id of the caller, for "own data only" checks (NFR-03). Null for an account without a staff profile.</summary>
    Guid? StaffMemberId { get; }

    string Email { get; }

    /// <summary>One of StaffRoles.</summary>
    string Role { get; }

    bool IsInRole(string role);
}