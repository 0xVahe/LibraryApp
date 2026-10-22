using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace LibraryApp.Infrastructure.Identity;

public sealed class StaffAccountStore(UserManager<AppUser> users, LibraryDbContext db) : IStaffAccountStore
{
    public Task<bool> EmailExistsAsync(string email, Guid? exceptUserId, CancellationToken ct)
    {
        var normalized = users.NormalizeEmail(email);
        return db.Users.AnyAsync(x => x.NormalizedEmail == normalized && (!exceptUserId.HasValue || x.Id != exceptUserId.Value), ct);
    }

    public async Task<IReadOnlyCollection<string>> GetExistingEmailsAsync(IReadOnlyCollection<string> emails,
        CancellationToken ct)
    {
        var normalizedEmails = emails.Select(users.NormalizeEmail).ToArray();
        return await db.Users.Where(x => x.NormalizedEmail != null && normalizedEmails.Contains(x.NormalizedEmail))
            .Select(x => x.Email!).ToListAsync(ct);
    }

    public async Task CreateAsync(Guid userId, string email, string password, string role, CancellationToken ct,
        bool requirePasswordChange = false)
    {
        RequireTransaction(ct);
        var user = new AppUser { Id = userId, Email = email, UserName = email };
        await EnsureUniqueAsync(() => users.CreateAsync(user, password));
        Ensure(await users.AddToRoleAsync(user, role));
        if (requirePasswordChange)
            Ensure(await users.AddClaimAsync(user,
                new Claim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString)));
    }

    public async Task UpdateEmailAsync(Guid userId, string email, CancellationToken ct)
    {
        RequireTransaction(ct);
        var user = await users.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("The staff member's account is missing.");
        user.Email = email;
        user.UserName = email;
        user.EmailConfirmed = false;
        user.SecurityStamp = Guid.NewGuid().ToString();
        // The email, normalized fields and stamp are persisted together in the caller's transaction.
        await EnsureUniqueAsync(() => users.UpdateAsync(user));
    }

    public Task<string> GetRoleAsync(Guid userId, CancellationToken ct) =>
        StaffAccountRoles.GetAsync(db, userId, ct);

    public async Task DeactivateAsync(Guid userId, CancellationToken ct)
    {
        RequireTransaction(ct);
        var user = await users.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("The staff member's account is missing.");
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.SecurityStamp = Guid.NewGuid().ToString();
        Ensure(await users.UpdateAsync(user));
    }

    private void RequireTransaction(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Account writes must run inside the staff management transaction.");
    }

    // Two requests can both pass the email check; the unique UserNameIndex and EmailIndex then refuse the second
    // write when Identity saves. Both indexes hold the email, so any unique violation here means the email is taken.
    private static async Task EnsureUniqueAsync(Func<Task<IdentityResult>> write)
    {
        IdentityResult result;
        try
        {
            result = await write();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new DomainException(StaffErrors.EmailTaken, ex);
        }
        Ensure(result);
    }
    
    private static void Ensure(IdentityResult result)
    {
        if (result.Succeeded)
            return;
        var codes = result.Errors.Select(x => x.Code).ToArray();
        var error = codes.Any(x => x is "DuplicateEmail" or "DuplicateUserName") ? StaffErrors.EmailTaken
            : codes.Contains("ConcurrencyFailure") ? CommonErrors.ConcurrencyConflict
            : StaffErrors.AccountWriteFailed;
        // Identity descriptions can contain personal data; expose only a stable module error.
        throw new DomainException(error);
    }
}
