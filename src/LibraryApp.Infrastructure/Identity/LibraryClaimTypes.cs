namespace LibraryApp.Infrastructure.Identity;

/// <summary>Custom claim names in the access token. Use with [Authorize(Roles = StaffRoles.Librarian)] etc.</summary>
public static class LibraryClaimTypes
{
    public const string Role = "role";
    public const string PasswordChangeRequired = "password_change_required";

    /// <summary>
    /// StaffMember.Id of the caller, for "own data only" checks (NFR-03). Login rejects accounts without a staff
    /// profile, so every token issued by POST /api/auth/login has it.
    /// </summary>
    public const string StaffMemberId = "staff_member_id";
}
