using Microsoft.AspNetCore.Identity;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>
/// Login account (X-09: Guid key, tables in LibraryDbContext).
/// Staff data lives in StaffMember, linked by StaffMember.UserId; every account has a StaffMember.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
}