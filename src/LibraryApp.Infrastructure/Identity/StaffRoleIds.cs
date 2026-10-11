using LibraryApp.Domain.Staff;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>
/// The three account roles with fixed ids. They are part of the model (<c>HasData</c> in IdentityConfiguration), so
/// the migration creates them in every environment: enrolment and login need them, not only the Development seed.
/// </summary>
public static class StaffRoleIds
{
    public static readonly Guid LibraryAdministrator = new("20000000-0000-0000-0005-000000000001");
    public static readonly Guid Librarian = new("20000000-0000-0000-0005-000000000002");
    public static readonly Guid StaffMember = new("20000000-0000-0000-0005-000000000003");

    public static readonly IReadOnlyList<(Guid Id, string Name)> All =
    [
        (LibraryAdministrator, StaffRoles.LibraryAdministrator),
        (Librarian, StaffRoles.Librarian),
        (StaffMember, StaffRoles.StaffMember),
    ];
}