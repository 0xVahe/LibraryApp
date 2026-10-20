using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>Identity does not rotate security stamps on role changes; save the new stamp with the role membership.</summary>
public sealed class StaffUserStore(LibraryDbContext db, IdentityErrorDescriber errors)
    : UserStore<AppUser, IdentityRole<Guid>, LibraryDbContext, Guid>(db, errors)
{
    public override async Task AddToRoleAsync(AppUser user, string normalizedRoleName, CancellationToken ct = default)
    {
        await base.AddToRoleAsync(user, normalizedRoleName, ct);
        user.SecurityStamp = Guid.NewGuid().ToString();
    }

    public override async Task RemoveFromRoleAsync(AppUser user, string normalizedRoleName, CancellationToken ct = default)
    {
        await base.RemoveFromRoleAsync(user, normalizedRoleName, ct);
        user.SecurityStamp = Guid.NewGuid().ToString();
    }
}
