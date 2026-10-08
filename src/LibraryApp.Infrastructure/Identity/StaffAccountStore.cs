using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Identity;

public sealed class StaffAccountStore(UserManager<AppUser> users, LibraryDbContext db) : IStaffAccountStore
{
    public Task<bool> EmailExistsAsync(string email, Guid? exceptUserId, CancellationToken ct)
    {
        var normalized = users.NormalizeEmail(email);
        return db.Users.AnyAsync(x => x.NormalizedEmail == normalized && (!exceptUserId.HasValue || x.Id != exceptUserId.Value), ct);
    }

    public async Task CreateAsync(Guid userId, string email, string password, string role, CancellationToken ct)
    {
        RequireTransaction(ct);
        var user = new AppUser { Id = userId, Email = email, UserName = email };
        Ensure(await users.CreateAsync(user, password));
        Ensure(await users.AddToRoleAsync(user, role));
    }

    public async Task UpdateEmailAsync(Guid userId, string email, CancellationToken ct)
    {
        RequireTransaction(ct);
        var user = await users.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("The staff member's account is missing.");
        user.Email = email;
        user.UserName = email;
        user.EmailConfirmed = false;
        // Both values and their normalized versions are persisted by one Identity update.
        Ensure(await users.UpdateAsync(user));
    }

    public Task<string> GetRoleAsync(Guid userId, CancellationToken ct) =>
        StaffAccountRoles.GetAsync(db, userId, ct);

    private void RequireTransaction(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Account writes must run inside the staff management transaction.");
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
