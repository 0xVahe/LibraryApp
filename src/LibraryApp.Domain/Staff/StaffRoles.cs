namespace LibraryApp.Domain.Staff;

/// <summary>The three roles of MEM-02. Names are stored in AspNetRoles: never rename.</summary>
public static class StaffRoles
{
    public const string LibraryAdministrator = "LibraryAdministrator";
    public const string Librarian = "Librarian";
    public const string StaffMember = "StaffMember";

    public static readonly IReadOnlyList<string> All = [LibraryAdministrator, Librarian, StaffMember];
}