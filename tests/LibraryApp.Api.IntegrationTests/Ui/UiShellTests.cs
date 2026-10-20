using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LibraryApp.Api.Ui;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Ui;

/// <summary>The real host with test endpoints outside /api, which behave like pages (cookie scheme, HTML errors).</summary>
public sealed class UiHostFactory : ApiHostFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Ui:SecurityStampValidationInterval", "00:00:00");   // check on every request
    }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddControllers().AddApplicationPart(typeof(UiTestController).Assembly);
}

[Route("ui-test")]
public sealed class UiTestController : Controller
{
    [HttpGet("library-staff")]
    [Authorize(Policy = UiPolicies.LibraryStaff)]
    public IActionResult LibraryStaff() => Content("library staff only");

    [HttpGet("boom")]
    [AllowAnonymous]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal detail");
}

public class UiShellTests(UiHostFactory factory) : IClassFixture<UiHostFactory>
{
    private static string Email(Guid staffMemberId) => StaffSeedData.People.Single(p => p.StaffMemberId == staffMemberId).Email;

    private static readonly string StaffMember = Email(StaffSeedIds.StaffMembers.AnnaPetrosyan);
    private static readonly string Librarian = Email(StaffSeedIds.StaffMembers.AramSargsyan);

    private Task<HttpClient> SignedIn(string email) => UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

    [Fact]
    public async Task An_anonymous_page_request_is_sent_to_sign_in_with_a_local_return_url()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
        Assert.Equal("?returnUrl=%2F", response.Headers.Location.Query);
    }

    [Theory]
    [InlineData(false, "/my-library")]
    [InlineData(true, "/desk")]
    public async Task The_start_page_depends_on_the_role(bool librarian, string expected)
    {
        using var client = await SignedIn(librarian ? Librarian : StaffMember);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_api_never_accepts_the_ui_cookie()
    {
        using var client = await SignedIn(StaffMember);

        var response = await client.GetAsync("/api/staff-members/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Pages_never_accept_the_access_token()
    {
        using var api = factory.CreateClient();
        var login = await api.PostAsJsonAsync("/api/auth/login", new { email = StaffMember, password = StaffSeedData.Password });
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString();
        var client = UiTestClient.Anonymous(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task A_role_without_access_is_sent_to_the_Forbidden_page()
    {
        using var staff = await SignedIn(StaffMember);
        using var librarian = await SignedIn(Librarian);

        var refused = await staff.GetAsync("/ui-test/library-staff");
        var allowed = await librarian.GetAsync("/ui-test/library-staff");

        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal("/forbidden", refused.Headers.Location!.AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData("/notfound", HttpStatusCode.NotFound, "Page not found")]
    [InlineData("/forbidden", HttpStatusCode.Forbidden, "have access to this page")]
    [InlineData("/no-such-page", HttpStatusCode.NotFound, "Page not found")]
    public async Task Status_pages_are_html_for_everyone(string path, HttpStatusCode status, string text)
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(text, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_api_route_stays_ProblemDetails()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync("/api/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_exception_on_a_page_shows_the_Error_page_with_a_trace_id_and_no_details()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync("/ui-test/boom");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Something went wrong", html);
        Assert.Contains("Reference for the library team", html);
        Assert.DoesNotContain("secret internal detail", html);
    }

    [Fact]
    public async Task Pages_get_the_security_headers_and_the_api_does_not()
    {
        var page = await UiTestClient.Anonymous(factory).GetAsync("/notfound");
        var api = await UiTestClient.Anonymous(factory).GetAsync("/api/no-such-endpoint");

        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(api.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Pages_allow_images_from_the_configured_cover_base_url()
    {
        var page = await UiTestClient.Anonymous(factory).GetAsync("/notfound");
        var baseUrl = factory.Services.GetRequiredService<IOptions<CatalogCoversOptions>>().Value.BaseUrl;
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains($"img-src 'self' data: {baseUrl};", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stylesheet_is_served_without_sign_in()
    {
        var response = await UiTestClient.Anonymous(factory).GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Status_pages_show_the_illustration_also_on_a_nested_path()
    {
        var client = UiTestClient.Anonymous(factory);

        var html = await (await client.GetAsync("/staff/00000000-0000-0000-0000-000000000000/nope")).Content.ReadAsStringAsync();
        var image = await client.GetAsync("/img/error-fallback.webp");

        Assert.Contains("<img class=\"error-art\" src=\"/img/error-fallback.webp\" alt=\"\"", html);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_stylesheet_has_the_agreed_brand_colours()
    {
        var css = await (await UiTestClient.Anonymous(factory).GetAsync("/css/site.css")).Content.ReadAsStringAsync();

        Assert.Contains("--green: #72bf44;", css);
        Assert.Contains("--green-hover: #5f9f39;", css);
        Assert.Contains(".btn-primary { background: var(--green); color: var(--white); }", css);
        Assert.DoesNotContain("--green-solid", css);
    }

    [Theory]
    [InlineData("/fonts/montserrat-latin-600-normal.woff2")]
    [InlineData("/fonts/montserrat-latin-700-normal.woff2")]
    [InlineData("/fonts/noto-sans-latin-400-normal.woff2")]
    [InlineData("/fonts/noto-sans-latin-600-normal.woff2")]
    public async Task The_fonts_of_the_stylesheet_are_served_locally(string path)
    {
        var css = await (await UiTestClient.Anonymous(factory).GetAsync("/css/site.css")).Content.ReadAsStringAsync();
        var response = await UiTestClient.Anonymous(factory).GetAsync(path);

        Assert.Contains($"url(\"..{path}\")", css);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("font/woff2", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_new_security_stamp_ends_the_cookie_session()
    {
        var email = Email(StaffSeedIds.StaffMembers.DavitHakobyan);
        using var client = await SignedIn(email);
        Assert.Equal("/my-library", (await client.GetAsync("/")).Headers.Location!.OriginalString);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByEmailAsync(email))!);   // what a password change does
        }

        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Sign_out_is_a_post_with_the_antiforgery_token()
    {
        var authCookie = await UiTestClient.CookieAsync(factory, StaffMember, StaffSeedData.Password);
        var client = UiTestClient.Anonymous(factory);

        // Any page with the layout has the sign-out form and its token; the antiforgery cookie comes with it.
        var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/forbidden");
        pageRequest.Headers.Add("Cookie", authCookie);
        var page = await client.SendAsync(pageRequest);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var cookies = $"{authCookie}; {UiTestClient.SetCookies(page)}";

        var withoutToken = new HttpRequestMessage(HttpMethod.Post, "/logout") { Content = new FormUrlEncodedContent([]) };
        withoutToken.Headers.Add("Cookie", cookies);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(withoutToken)).StatusCode);

        var signOut = new HttpRequestMessage(HttpMethod.Post, "/logout")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        signOut.Headers.Add("Cookie", cookies);
        var response = await client.SendAsync(signOut);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("LibraryApp.Auth=;", StringComparison.Ordinal));
    }

    /// <summary>
    /// The page was opened while signed in, then the session ended (cookie expired or a new security stamp): its
    /// sign-out form still works and leads to the sign-in page instead of an error.
    /// </summary>
    [Fact]
    public async Task Sign_out_after_the_session_ended_goes_to_sign_in()
    {
        var authCookie = await UiTestClient.CookieAsync(factory, StaffMember, StaffSeedData.Password);
        var client = UiTestClient.Anonymous(factory);
        var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/forbidden");
        pageRequest.Headers.Add("Cookie", authCookie);
        var page = await client.SendAsync(pageRequest);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        // Only the antiforgery cookie is left: the authentication cookie is gone with the session.
        var signOut = new HttpRequestMessage(HttpMethod.Post, "/logout")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        signOut.Headers.Add("Cookie", UiTestClient.SetCookies(page));
        var response = await client.SendAsync(signOut);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.OriginalString);
    }

    /// <summary>NFR-02 for the UI: only the sign-in, sign-out and status pages are open; every other page asks to sign in.</summary>
    [Fact]
    public async Task Every_page_requires_sign_in_except_the_allowed_ones()
    {
        string[] allowed = [UiPaths.Login, UiPaths.ChangePassword, UiPaths.Logout, UiPaths.Error, UiPaths.NotFound, UiPaths.Forbidden];
        var pages = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<PageActionDescriptor>() is not null)
            .ToList();
        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            var route = "/" + page.RoutePattern.RawText!.TrimStart('/');
            if (page.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                Assert.Contains(route, allowed);
                continue;
            }

            var path = Regex.Replace(route, "{[^}]*guid[^}]*}", Guid.NewGuid().ToString());
            path = Regex.Replace(path, "{[^}]*}", "1");
            var response = await UiTestClient.Anonymous(factory).GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.AbsolutePath == UiPaths.Login,
                $"GET {path} returned {(int)response.StatusCode}; expected a redirect to sign-in.");
        }
    }

    [Fact]
    public void No_page_route_uses_a_reserved_prefix()
    {
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<PageActionDescriptor>() is not null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .ToList();

        Assert.Contains("/error", routes);
        Assert.All(routes, route => Assert.DoesNotContain(UiPaths.Reserved,
            prefix => route.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                      || route.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)));
    }
}
