using LibraryApp.Application.Staff.Ports;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Identity;

public sealed class UserAccountStore(UserManager<AppUser> userManager, LibraryDbContext db) : IUserAccountStore
{
    public async Task<LoginAccount?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        var staff = await db.StaffMembers
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(ct);

        return new LoginAccount(user.Id, user.Email!, staff?.Id, staff?.Status, roles.ToList());
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || await userManager.IsLockedOutAsync(user))
            return false;

        var valid = await userManager.CheckPasswordAsync(user, password);
        if (valid)
            await userManager.ResetAccessFailedCountAsync(user);
        else
            await userManager.AccessFailedAsync(user);   // Identity default: 5 failures → 5-minute lockout

        return valid;
    }
}