using System.Security.Claims;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Api.Ui.Auth;

/// <summary>
/// Cookie sign-in of the pages. The Login page calls <c>IAuthService.LoginAsync</c> exactly like the API (corporate
/// email, lockout, deactivated accounts), then passes the answer here. The cookie gets the claims of the issued access
/// token — the same code builds both — plus the account's security stamp, and expires with the token.
/// </summary>
public interface IUiSignIn
{
    Task SignInAsync(HttpContext context, LoginResponse login);

    Task SignOutAsync(HttpContext context);
}

public sealed class UiSignIn(
    IOptionsMonitor<JwtBearerOptions> bearerOptions,
    UserManager<AppUser> users,
    IOptions<IdentityOptions> identityOptions) : IUiSignIn
{
    // Claims of the access token that describe the user; issuer, audience, times and the token id stay out of the cookie.
    private static readonly HashSet<string> UserClaims =
    [
        JwtRegisteredClaimNames.Sub,
        JwtRegisteredClaimNames.Email,
        JwtRegisteredClaimNames.Name,   // "First Last" of the staff member, shown in the user chip
        LibraryClaimTypes.Role,
        LibraryClaimTypes.StaffMemberId,
        LibraryClaimTypes.PasswordChangeRequired,
    ];

    private static readonly JsonWebTokenHandler Tokens = new() { MapInboundClaims = false };

    public async Task SignInAsync(HttpContext context, LoginResponse login)
    {
        var principal = await CreatePrincipalAsync(login);
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = DateTime.SpecifyKind(login.ExpiresAtUtc, DateTimeKind.Utc),
            AllowRefresh = false,   // the session ends with the access token, never later
            IsPersistent = false,
        };
        await context.SignInAsync(UiAuthentication.CookieScheme, principal, properties);
    }

    public Task SignOutAsync(HttpContext context) => context.SignOutAsync(UiAuthentication.CookieScheme);

    /// <summary>The cookie principal for a successful login. Public for the test helper that signs seeded users in.</summary>
    public async Task<ClaimsPrincipal> CreatePrincipalAsync(LoginResponse login)
    {
        // The token was issued a moment ago by this host; validating it again is the safe way to read its claims.
        var parameters = bearerOptions.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var result = await Tokens.ValidateTokenAsync(login.AccessToken, parameters);
        if (!result.IsValid)
            throw new InvalidOperationException("The access token of a successful login could not be read.", result.Exception);

        var claims = result.ClaimsIdentity.Claims.Where(c => UserClaims.Contains(c.Type)).ToList();
        var userId = claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        var user = await users.FindByIdAsync(userId)
                   ?? throw new InvalidOperationException("The signed-in account no longer exists.");
        claims.Add(new Claim(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, await users.GetSecurityStampAsync(user)));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims, UiAuthentication.CookieScheme, JwtRegisteredClaimNames.Email, LibraryClaimTypes.Role));
    }
}
