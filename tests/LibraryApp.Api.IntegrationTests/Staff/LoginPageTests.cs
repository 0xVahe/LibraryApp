using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using LibraryApp.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>A host whose login limit is reached after two POSTs.</summary>
public sealed class LoginRateLimitHostFactory : ApiHostFactory
{
    public const int PermitLimit = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("LoginRateLimit:PermitLimit", PermitLimit.ToString());
    }
}

/// <summary>The Login page like a browser: GET the form (antiforgery token and cookie), then POST it.</summary>
internal static class LoginPage
{
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string email, string password, string query = "")
    {
        var form = await client.GetAsync("/login" + query);
        var token = Regex.Match(await form.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var request = new HttpRequestMessage(HttpMethod.Post, "/login" + query)
        {
            Content = new FormUrlEncodedContent([
                new("__RequestVerificationToken", token),
                new("Input.Email", email),
                new("Input.Password", password),
            ]),
        };
        request.Headers.Add("Cookie", UiTestClient.SetCookies(form));
        return await client.SendAsync(request);
    }

    public static bool SetsAuthCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) &&
        values.Any(v => v.StartsWith("LibraryApp.Auth=", StringComparison.Ordinal) && !v.StartsWith("LibraryApp.Auth=;", StringComparison.Ordinal));

    public static string Email(Guid staffMemberId) => StaffSeedData.People.Single(p => p.StaffMemberId == staffMemberId).Email;
}

public class LoginPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task A_correct_login_sets_the_cookie_and_signs_the_user_in()
    {
        var client = UiTestClient.Anonymous(factory);
        var response = await LoginPage.PostAsync(client, LoginPage.Email(StaffSeedIds.StaffMembers.AnnaPetrosyan), StaffSeedData.Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.True(LoginPage.SetsAuthCookie(response));

        // With the cookie the start page sends a Staff Member to My library, not to sign-in.
        var start = new HttpRequestMessage(HttpMethod.Get, "/");
        start.Headers.Add("Cookie", UiTestClient.SetCookies(response));
        Assert.Equal("/my-library", (await client.SendAsync(start)).Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("?returnUrl=%2Fcatalog", "/catalog")]
    [InlineData("?returnUrl=https%3A%2F%2Fevil.example%2F", "/")]
    [InlineData("?returnUrl=%2F%2Fevil.example", "/")]
    [InlineData("?returnUrl=%2F%5Cevil.example", "/")]
    public async Task Only_a_local_return_url_is_followed(string query, string expected)
    {
        var response = await LoginPage.PostAsync(UiTestClient.Anonymous(factory),
            LoginPage.Email(StaffSeedIds.StaffMembers.AnnaPetrosyan), StaffSeedData.Password, query);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_wrong_password_shows_the_error_and_the_lockout_rule()
    {
        var response = await LoginPage.PostAsync(UiTestClient.Anonymous(factory),
            LoginPage.Email(StaffSeedIds.StaffMembers.DavitHakobyan), "Wrong-password-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password.", html);
        Assert.Contains("After 5 wrong attempts the account is locked for 5 minutes.", html);
        Assert.False(LoginPage.SetsAuthCookie(response));
    }

    [Fact]
    public async Task A_deactivated_account_sees_the_message_on_the_form()
    {
        var response = await LoginPage.PostAsync(UiTestClient.Anonymous(factory),
            LoginPage.Email(StaffSeedIds.StaffMembers.HaykManukyan), StaffSeedData.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This account is deactivated.", await response.Content.ReadAsStringAsync());
        Assert.False(LoginPage.SetsAuthCookie(response));
    }

    [Fact]
    public async Task A_non_corporate_email_is_a_field_error()
    {
        var response = await LoginPage.PostAsync(UiTestClient.Anonymous(factory), "someone@gmail.com", StaffSeedData.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"email-error\"", html);
        Assert.Contains("Only Ameriabank staff can log in.", html);
    }
}

public class LoginRateLimitPageTests(LoginRateLimitHostFactory factory) : IClassFixture<LoginRateLimitHostFactory>
{
    [Fact]
    public async Task A_rejected_post_goes_back_to_the_form_with_too_many_attempts()
    {
        var client = UiTestClient.Anonymous(factory);
        var email = LoginPage.Email(StaffSeedIds.StaffMembers.AnnaPetrosyan);
        for (var i = 0; i < LoginRateLimitHostFactory.PermitLimit; i++)
            await LoginPage.PostAsync(client, email, "Wrong-password-1", "?returnUrl=%2Fcatalog");

        // Even the right password is rejected now; the GETs of the form were never counted.
        var rejected = await LoginPage.PostAsync(client, email, StaffSeedData.Password, "?returnUrl=%2Fcatalog");
        Assert.Equal(HttpStatusCode.SeeOther, rejected.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fcatalog&tooManyAttempts=true", rejected.Headers.Location!.OriginalString);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.False(LoginPage.SetsAuthCookie(rejected));

        var page = await client.GetAsync(rejected.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Too many attempts", await page.Content.ReadAsStringAsync());
    }
}

public class TooManyAttemptsUrlTests
{
    [Theory]
    [InlineData("/login", "?returnUrl=%2Fcatalog", "/login?returnUrl=%2Fcatalog&tooManyAttempts=true")]
    [InlineData("/change-password", "", "/change-password?tooManyAttempts=true")]
    [InlineData("/change-password", "?tooManyAttempts=true", "/change-password?tooManyAttempts=true")]
    public void A_rejected_page_post_goes_back_to_its_own_page(string path, string query, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        Assert.Equal(expected, LoginRateLimiting.TooManyAttemptsUrl(context.Request));
    }
}