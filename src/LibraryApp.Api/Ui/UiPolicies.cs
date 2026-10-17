using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;

namespace LibraryApp.Api.Ui;

/// <summary>
/// Authorization policies of the pages. Every page needs <see cref="SignedIn"/> (set for all pages in
/// <see cref="UiServiceCollectionExtensions.MapUi"/>); a page for library staff adds
/// <c>[Authorize(Policy = UiPolicies.LibraryStaff)]</c>. Pages never name an authentication scheme: the path chooses it.
/// </summary>
public static class UiPolicies
{
    public const string SignedIn = "ui-signed-in";
    public const string LibraryStaff = "ui-library-staff";
    public const string Administrator = "ui-administrator";

    internal static void Add(AuthorizationOptions options)
    {
        options.AddPolicy(SignedIn, policy => policy.RequireAuthenticatedUser());
        options.AddPolicy(LibraryStaff, policy => policy.RequireRole(StaffRoles.Librarian, StaffRoles.LibraryAdministrator));
        options.AddPolicy(Administrator, policy => policy.RequireRole(StaffRoles.LibraryAdministrator));
    }
}
