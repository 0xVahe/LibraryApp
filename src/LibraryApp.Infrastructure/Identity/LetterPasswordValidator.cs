using Microsoft.AspNetCore.Identity;

namespace LibraryApp.Infrastructure.Identity;

/// <summary>BR-MEM-5: a password needs at least one letter (Identity has no built-in option for it).</summary>
public sealed class LetterPasswordValidator : IPasswordValidator<AppUser>
{
    public static readonly IdentityError MissingLetter = new()
    {
        Code = "PasswordRequiresLetter",
        Description = "Passwords must have at least one letter."
    };

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password) =>
        Task.FromResult(password is not null && password.Any(char.IsLetter)
            ? IdentityResult.Success
            : IdentityResult.Failed(MissingLetter));
}