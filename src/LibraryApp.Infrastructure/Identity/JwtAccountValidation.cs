using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>Checks the account behind a signed JWT, with a bounded delay for revocation (MEM-02, NFR-02).</summary>
public sealed class JwtAccountValidation(LibraryDbContext db, IMemoryCache cache, IOptions<JwtOptions> options)
{
    public async Task ValidateAsync(TokenValidatedContext context)
    {
        var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var stamp = context.Principal?.FindFirst(LibraryClaimTypes.SecurityStamp)?.Value;
        if (!Guid.TryParse(subject, out var userId) || string.IsNullOrWhiteSpace(stamp))
        {
            context.Fail("The account is no longer valid.");
            return;
        }

        // A new login has a new stamp and reads fresh state, even while an older token is cached.
        var key = (typeof(JwtAccountValidation), userId, stamp);
        var state = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.SecurityStampCacheSeconds);
            return await (from user in db.Users.AsNoTracking()
                          join staff in db.StaffMembers.AsNoTracking() on user.Id equals staff.UserId
                          where user.Id == userId
                          select new AccountState(user.SecurityStamp, staff.Status))
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        });

        if (state is null || state.Status == StaffMemberStatus.Deactivated ||
            !string.Equals(state.SecurityStamp, stamp, StringComparison.Ordinal))
            context.Fail("The account is no longer valid.");
    }

    private sealed record AccountState(string? SecurityStamp, StaffMemberStatus Status);
}
