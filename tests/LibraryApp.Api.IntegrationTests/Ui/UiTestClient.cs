using LibraryApp.Api.Ui.Auth;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Ui;

/// <summary>
/// Page tests of every group: a client signed in with the UI cookie as a seeded user, without a Login page. The user
/// logs in through <see cref="IAuthService"/> like on the real Login page, and the cookie carries the same claims.
/// Redirects are not followed, so a test sees where a page sends the user.
/// </summary>
public static class UiTestClient
{
    public static HttpClient Anonymous(ApiHostFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public static async Task<HttpClient> SignedInAsync(ApiHostFactory factory, string email, string password)
    {
        var client = Anonymous(factory);
        client.DefaultRequestHeaders.Add("Cookie", await CookieAsync(factory, email, password));
        return client;
    }

    /// <summary>The <c>Cookie</c> header value of a signed-in user (name=value of the UI authentication cookie).</summary>
    public static async Task<string> CookieAsync(ApiHostFactory factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var login = await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync(new LoginRequest(email, password))
                    ?? throw new InvalidOperationException($"The seeded user {email} could not log in.");
        var signIn = (UiSignIn)scope.ServiceProvider.GetRequiredService<IUiSignIn>();
        var principal = await signIn.CreatePrincipalAsync(login);

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(UiAuthentication.CookieScheme);
        var properties = new AuthenticationProperties { ExpiresUtc = login.ExpiresAtUtc, IssuedUtc = DateTimeOffset.UtcNow, AllowRefresh = false };
        var ticket = new AuthenticationTicket(principal, properties, UiAuthentication.CookieScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    /// <summary>Cookies set by a response, as one <c>Cookie</c> header value (name=value pairs).</summary>
    public static string SetCookies(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0])
            : []);
}
