using System.Security.Claims;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Api.Ui.Shared;

public sealed record NavItem(string Text, string Path);

/// <summary>Main navigation and the user chip of <c>_Layout</c>, by role (UI guide, Part 1 "Shared").</summary>
public static class UiNavigation
{
    public const string MyLibrary = "/my-library";
    public const string Catalog = "/catalog";
    public const string Notifications = "/notifications";
    public const string LoanDesk = "/desk";
    public const string Staff = "/staff";
    public const string Reports = "/reports";
    public const string Settings = "/settings";
    public const string Profile = "/profile";

    /// <summary>
    /// Staff Member: My library, Catalog, Notifications. Librarian: Loan desk, Staff members, Catalog, Reports, My library,
    /// Notifications. Library Administrator: the Librarian items plus Settings. Nothing while the first-login password
    /// change is pending.
    /// </summary>
    public static IReadOnlyList<NavItem> For(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || user.HasClaim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString))
            return [];

        if (!IsLibraryStaff(user))
            return [new("My library", MyLibrary), new("Catalog", Catalog), new("Notifications", Notifications)];

        var items = new List<NavItem>
        {
            new("Loan desk", LoanDesk),
            new("Staff members", Staff),
            new("Catalog", Catalog),
            new("Reports", Reports),
            new("My library", MyLibrary),
            new("Notifications", Notifications),
        };
        if (user.IsInRole(StaffRoles.LibraryAdministrator))
            items.Add(new("Settings", Settings));
        return items;
    }

    /// <summary>The start page of the role: the Loan desk for library staff, My library for everyone else.</summary>
    public static string StartPath(ClaimsPrincipal user) => IsLibraryStaff(user) ? LoanDesk : MyLibrary;

    /// <summary>The nav item the current path belongs to (its own path or a page below it).</summary>
    public static bool IsCurrent(NavItem item, PathString path) =>
        path.StartsWithSegments(item.Path, StringComparison.OrdinalIgnoreCase);

    public static string Email(ClaimsPrincipal user) => user.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? "";

    /// <summary>
    /// The name in the user chip: the <c>name</c> claim when the access token carries one; until then the corporate
    /// email's name.surname part ("anna.petrosyan@…" → "Anna Petrosyan").
    /// </summary>
    public static string DisplayName(ClaimsPrincipal user) =>
        user.FindFirst(JwtRegisteredClaimNames.Name)?.Value is { Length: > 0 } name
            ? name
            : string.Join(' ', NameParts(user).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>"anna.petrosyan@…" → "AP".</summary>
    public static string Initials(ClaimsPrincipal user) =>
        string.Concat(NameParts(user).Take(2).Select(part => char.ToUpperInvariant(part[0])));

    private static string[] NameParts(ClaimsPrincipal user) =>
        Email(user).Split('@')[0].Split('.', StringSplitOptions.RemoveEmptyEntries);

    public static string RoleName(ClaimsPrincipal user) =>
        user.IsInRole(StaffRoles.LibraryAdministrator) ? "Library administrator"
        : user.IsInRole(StaffRoles.Librarian) ? "Librarian"
        : "Staff member";

    private static bool IsLibraryStaff(ClaimsPrincipal user) =>
        user.IsInRole(StaffRoles.Librarian) || user.IsInRole(StaffRoles.LibraryAdministrator);
}
