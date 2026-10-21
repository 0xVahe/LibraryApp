using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using System.Security.Claims;
using LibraryApp.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Identity;

public sealed class UserAccountStore(
    UserManager<AppUser> userManager,
    IPasswordHasher<AppUser> passwordHasher,
    LibraryDbContext db,
    IClock clock) : IUserAccountStore
{
    // Hash of a random password, checked when the email is unknown: that path then costs as much as a wrong password.
    private static string? _dummyPasswordHash;

    public async Task<LoginAccount?> VerifyCredentialsAsync(string email, string password, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            VerifyDummyPassword(password);
            return null;
        }

        // The password is checked for a locked-out account too, so the answer time does not reveal the lockout.
        var lockedOut = await userManager.IsLockedOutAsync(user);
        var valid = await userManager.CheckPasswordAsync(user, password);
        if (!valid)
        {
            if (!lockedOut)
                await RegisterFailedAttemptAsync(user.Id, ct);
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        var staff = await db.StaffMembers
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => new { x.Id, x.Status })
            .SingleOrDefaultAsync(ct);

        // Correct credentials for a deactivated member reach AuthService's AccountDeactivated error,
        // even with permanent Identity lockout. Temporary lockouts still reject all sign-ins.
        if (lockedOut && staff?.Status != StaffMemberStatus.Deactivated)
            return null;

        if (!lockedOut && user.AccessFailedCount > 0)
            await ResetFailedAttemptsAsync(user.Id, ct);

        var mustChangePassword = (await userManager.GetClaimsAsync(user)).Any(IsPasswordChangeMarker);
        return new LoginAccount(user.Id, user.Email!, staff?.Id, staff?.Status, roles.ToList(),
            await userManager.GetSecurityStampAsync(user), mustChangePassword);
    }

    public async Task<bool> VerifyCurrentPasswordAsync(Guid userId, string currentPassword, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            VerifyDummyPassword(currentPassword);
            return false;
        }

        // The password is checked for a locked-out account too, so the answer time does not reveal the lockout.
        var lockedOut = await userManager.IsLockedOutAsync(user);
        var valid = await userManager.CheckPasswordAsync(user, currentPassword);
        if (lockedOut)
            return false;
        if (valid)
            return true;

        await RegisterFailedAttemptAsync(user.Id, ct);
        return false;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        var changed = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
            return false;

        Ensure(await userManager.ResetAccessFailedCountAsync(user));

        var marker = (await userManager.GetClaimsAsync(user)).FirstOrDefault(IsPasswordChangeMarker);
        if (marker is not null)
            Ensure(await userManager.RemoveClaimAsync(user, marker));

        return true;
    }

    private static bool IsPasswordChangeMarker(Claim claim) =>
        claim.Type == LibraryClaimTypes.PasswordChangeRequired && claim.Value == bool.TrueString;

    // Throwing rolls back the caller's transaction, so the password never changes without the rest.
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new DomainException(StaffErrors.AccountWriteFailed);
    }

    /// <summary>
    /// One UPDATE, so parallel wrong passwords are all counted (UserManager.AccessFailedAsync loses them on a
    /// concurrency conflict). SQL Server evaluates every SET with the values from before the update, so the count and
    /// LockoutEnd always agree. Same rule as Identity: MaxFailedAccessAttempts failures → locked for
    /// DefaultLockoutTimeSpan (defaults: 5 failures, 5 minutes) and the count goes back to 0.
    /// </summary>
    private Task RegisterFailedAttemptAsync(Guid userId, CancellationToken ct)
    {
        var lockout = userManager.Options.Lockout;
        var maxAttempts = lockout.MaxFailedAccessAttempts;
        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        DateTimeOffset? lockoutEnd = now.Add(lockout.DefaultLockoutTimeSpan);
        var stamp = Guid.NewGuid().ToString();

        // Not while locked out: requests still in flight when the lockout starts must not count toward the next one.
        return db.Users
            .Where(u => u.Id == userId && (u.LockoutEnd == null || u.LockoutEnd <= now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutEnd,
                    u => u.LockoutEnabled && u.AccessFailedCount + 1 >= maxAttempts ? lockoutEnd : u.LockoutEnd)
                .SetProperty(u => u.AccessFailedCount,
                    u => u.LockoutEnabled && u.AccessFailedCount + 1 >= maxAttempts ? 0 : u.AccessFailedCount + 1)
                .SetProperty(u => u.ConcurrencyStamp, stamp), ct);
    }

    private Task ResetFailedAttemptsAsync(Guid userId, CancellationToken ct)
    {
        var stamp = Guid.NewGuid().ToString();

        return db.Users
            .Where(u => u.Id == userId && u.AccessFailedCount > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AccessFailedCount, 0)
                .SetProperty(u => u.ConcurrencyStamp, stamp), ct);
    }

    private void VerifyDummyPassword(string password)
    {
        var hash = _dummyPasswordHash ??= passwordHasher.HashPassword(new AppUser(), Guid.NewGuid().ToString("N"));
        passwordHasher.VerifyHashedPassword(new AppUser(), hash, password);
    }
}
