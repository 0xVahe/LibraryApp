using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Api.Ui.Auth;

/// <summary>
/// Ends a cookie session when the account changed: Identity gives the account a new security stamp on every password
/// change, and the deactivation of a staff member sets one too. The stamp in the cookie is compared with the stored one
/// at most once per <see cref="UiOptions.SecurityStampValidationInterval"/> per account and stamp; a match is
/// remembered in memory, so the cookie itself is never rewritten and never outlives the access token.
/// <para>Without <c>SignInManager</c> (the system uses <c>AddIdentityCore</c>), Identity's own validator is not available.</para>
/// </summary>
public static class SecurityStampCheck
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var interval = services.GetRequiredService<IOptions<UiOptions>>().Value.SecurityStampValidationInterval;
        var stampClaim = services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;

        var userId = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var stamp = context.Principal?.FindFirst(stampClaim)?.Value;
        if (userId is null || stamp is null)
        {
            await RejectAsync(context);
            return;
        }

        var cache = services.GetRequiredService<IMemoryCache>();
        var key = $"ui:security-stamp:{userId}:{stamp}";
        if (interval > TimeSpan.Zero && cache.TryGetValue(key, out _))
            return;

        var users = services.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId);
        if (user is null || !string.Equals(await users.GetSecurityStampAsync(user), stamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
            return;
        }

        if (interval > TimeSpan.Zero)
            cache.Set(key, true, interval);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.RequestServices.GetRequiredService<IUiSignIn>().SignOutAsync(context.HttpContext);
    }
}
