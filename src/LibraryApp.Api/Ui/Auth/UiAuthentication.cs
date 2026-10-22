using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.Ui.Auth;

/// <summary>
/// The cookie scheme of the pages and the scheme selector in front of it: <c>/api/*</c> authenticates with the JWT
/// only, every other path with the cookie only. A browser sends cookies by itself, so an API that accepted the cookie
/// could be called from another site (cross-site request forgery); the selector rules that out.
/// </summary>
public static class UiAuthentication
{
    public const string CookieScheme = "LibraryApp.Cookie";
    public const string SelectorScheme = "LibraryApp";

    internal static void Add(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddAuthentication()
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "LibraryApp.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // Development (and the test host) also runs on plain HTTP; everywhere else the cookie is HTTPS only.
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.SlidingExpiration = false;
                options.LoginPath = UiPaths.Login;
                options.LogoutPath = UiPaths.Logout;
                options.AccessDeniedPath = UiPaths.Forbidden;
                options.ReturnUrlParameter = "returnUrl";
                options.Events.OnValidatePrincipal = SecurityStampCheck.ValidateAsync;
            })
            .AddPolicyScheme(SelectorScheme, "JWT for /api, cookie for pages", options =>
                options.ForwardDefaultSelector = context =>
                    UiPaths.IsApi(context) ? JwtBearerDefaults.AuthenticationScheme : CookieScheme);

        // Same session length as the access token (one setting, Jwt:AccessTokenMinutes); a sign-in sets the exact end.
        services.AddOptions<CookieAuthenticationOptions>(CookieScheme)
            .Configure<IOptions<JwtOptions>>((cookie, jwt) => cookie.ExpireTimeSpan = TimeSpan.FromMinutes(jwt.Value.AccessTokenMinutes));

        // After every module: the selector is the default scheme (JWT for /api, cookie for pages).
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = SelectorScheme;
            options.DefaultAuthenticateScheme = SelectorScheme;
            options.DefaultChallengeScheme = SelectorScheme;
            options.DefaultForbidScheme = SelectorScheme;
            options.DefaultSignInScheme = CookieScheme;
            options.DefaultSignOutScheme = CookieScheme;
        });
    }
}
