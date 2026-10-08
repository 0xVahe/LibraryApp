using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>Staff management requires exactly one of the three supported account roles.</summary>
internal static class StaffAccountRoles
{
    public static async Task<string> GetAsync(LibraryDbContext db, Guid userId, CancellationToken ct)
    {
        var roles = await (from userRole in db.UserRoles.AsNoTracking()
                           join role in db.Roles on userRole.RoleId equals role.Id
                           where userRole.UserId == userId
                           select role.Name).ToListAsync(ct);

        if (roles.Count != 1 || roles[0] is not { } name || !StaffRoles.All.Contains(name))
            throw new DomainException(StaffErrors.AccountRoleInvalid);

        return name;
    }
}
