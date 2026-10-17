using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using LibraryApp.Api.Ui.Auth;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Ui;

/// <summary>Test endpoints outside /api that use the shell the way the Login page and the pages will.</summary>
[Route("ui-test")]
public sealed class UiSignInTestController(IAuthService auth, IUiSignIn signIn) : Controller
{
    public sealed record Credentials(string Email, string Password);

    [HttpPost("sign-in")]
    [AllowAnonymous]
    public async Task<IActionResult> SignIn([FromBody] Credentials credentials)
    {
        var login = await auth.LoginAsync(new LoginRequest(credentials.Email, credentials.Password));
        if (login is null)
            return Unauthorized();
        await signIn.SignInAsync(HttpContext, login);
        return NoContent();
    }

    [HttpPost("boom")]
    [AllowAnonymous]
    public IActionResult BoomOnPost() => throw new InvalidOperationException("secret internal detail");
}

/// <summary>Behaviour the shell promises (README, integration answers, comments) that the first tests did not cover.</summary>
public class UiShellAuditTests(UiHostFactory factory) : IClassFixture<UiHostFactory>
{
    private static readonly string StaffMember =
        StaffSeedData.People.Single(p => p.StaffMemberId == StaffSeedIds.StaffMembers.MariamAvetisyan).Email;

    private CookieAuthenticationOptions CookieOptions =>
        factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(UiAuthentication.CookieScheme);

    /// <summary>The real sign-in (what the Login page calls): cookie flags, the ticket ends with the token, no refresh.</summary>
    [Fact]
    public async Task Sign_in_sets_a_session_cookie_that_ends_with_the_access_token()
    {
        var client = UiTestClient.Anonymous(factory);

        var response = await client.PostAsJsonAsync("/ui-test/sign-in", new { email = StaffMember, password = StaffSeedData.Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("LibraryApp.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);   // not kept after the browser closes

        var ticket = CookieOptions.TicketDataFormat.Unprotect(setCookie.Split(';')[0]["LibraryApp.Auth=".Length..])!;
        Assert.False(ticket.Properties.AllowRefresh);
        Assert.InRange(ticket.Properties.ExpiresUtc!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(61));
        Assert.Equal(StaffMember, ticket.Principal.FindFirst("email")?.Value);
        Assert.NotNull(ticket.Principal.FindFirst("staff_member_id"));
        Assert.Null(ticket.Principal.FindFirst("jti"));   // token-only claims stay out

        var start = new HttpRequestMessage(HttpMethod.Get, "/");
        start.Headers.Add("Cookie", setCookie.Split(';')[0]);
        Assert.Equal("/my-library", (await client.SendAsync(start)).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task An_expired_cookie_asks_to_sign_in_again()
    {
        var cookie = await CookieWith(p => { p.IssuedUtc = DateTimeOffset.UtcNow.AddHours(-2); p.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1); });
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);

        var response = await UiTestClient.Anonymous(factory).SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task The_return_url_keeps_path_and_query()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync("/?search=risk&page=2");   // "/" is a protected page

        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
        Assert.Equal("?returnUrl=%2F%3Fsearch%3Drisk%26page%3D2", response.Headers.Location.Query);
    }

    [Fact]
    public async Task An_exception_in_a_post_also_shows_the_Error_page()
    {
        var response = await UiTestClient.Anonymous(factory).PostAsync("/ui-test/boom", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret internal detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_missing_static_file_is_the_Not_found_page()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync("/css/no-such.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An account with the first-login flag (imported staff) on the pages: the error, not-found and sign-out pages
    /// must keep working for it (UI guide Part 0 "First login"; until Group 2 adds the redirect to /change-password).
    /// </summary>
    [Theory]
    [InlineData("GET", "/notfound", HttpStatusCode.NotFound)]
    [InlineData("GET", "/error", HttpStatusCode.OK)]
    public async Task The_status_pages_work_for_a_first_login_account(string method, string path, HttpStatusCode expected)
    {
        var cookie = await CookieWith(_ => { }, firstLogin: true);
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Cookie", cookie);
        if (method == "POST")
            request.Content = new FormUrlEncodedContent([]);

        var response = await UiTestClient.Anonymous(factory).SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_first_login_account_can_sign_out()
    {
        var cookie = await CookieWith(_ => { }, firstLogin: true);
        var client = UiTestClient.Anonymous(factory);
        var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/notfound");
        pageRequest.Headers.Add("Cookie", cookie);
        var page = await client.SendAsync(pageRequest);
        var token = System.Text.RegularExpressions.Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var signOut = new HttpRequestMessage(HttpMethod.Post, "/logout")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        signOut.Headers.Add("Cookie", $"{cookie}; {UiTestClient.SetCookies(page)}");
        var response = await client.SendAsync(signOut);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Any_other_page_sends_a_first_login_account_to_the_password_change()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", await CookieWith(_ => { }, firstLogin: true));

        var response = await UiTestClient.Anonymous(factory).SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/change-password", response.Headers.Location!.OriginalString);
    }

    private async Task<string> CookieWith(Action<AuthenticationProperties> change, bool firstLogin = false)
    {
        using var scope = factory.Services.CreateScope();
        var login = (await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync(new LoginRequest(StaffMember, StaffSeedData.Password)))!;
        var principal = await ((UiSignIn)scope.ServiceProvider.GetRequiredService<IUiSignIn>()).CreatePrincipalAsync(login);
        if (firstLogin)
        {
            var identity = (ClaimsIdentity)principal.Identity!;
            foreach (var role in identity.FindAll(LibraryClaimTypes.Role).ToList())
                identity.RemoveClaim(role);
            identity.AddClaim(new Claim(LibraryClaimTypes.PasswordChangeRequired, bool.TrueString));
        }

        var properties = new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = login.ExpiresAtUtc, AllowRefresh = false };
        change(properties);
        var ticket = new AuthenticationTicket(principal, properties, UiAuthentication.CookieScheme);
        return $"{CookieOptions.Cookie.Name}={CookieOptions.TicketDataFormat.Protect(ticket)}";
    }
}
