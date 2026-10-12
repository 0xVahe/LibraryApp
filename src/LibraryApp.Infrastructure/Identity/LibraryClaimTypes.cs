namespace LibraryApp.Infrastructure.Identity;

/// <summary>Custom claim names in the access token. Use with [Authorize(Roles = StaffRoles.Librarian)] etc.</summary>
public static class LibraryClaimTypes
{
    public const string Role = "role";
    public const string PasswordChangeRequired = "password_change_required";

    /// <summary>StaffMember.Id of the caller, for "own data only" checks (NFR-03).</summary>
    public const string StaffMemberId = "staff_member_id";
}
