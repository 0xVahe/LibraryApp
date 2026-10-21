using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class ChangePasswordPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly string Email = LoginPage.Email(StaffSeedIds.StaffMembers.AnnaPetrosyan);

    [Fact]
    public async Task A_signed_in_user_sees_the_password_form()
    {
        var client = await UiTestClient.SignedInAsync(factory, Email, StaffSeedData.Password);

        var response = await client.GetAsync("/change-password");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.CurrentPassword\"", html);
        Assert.Contains("name=\"RepeatPassword\"", html);
    }

    [Fact]
    public async Task A_different_repeat_password_is_shown_on_the_form_and_changes_nothing()
    {
        var client = UiTestClient.Anonymous(factory);
        var cookie = await UiTestClient.CookieAsync(factory, Email, StaffSeedData.Password);
        var form = new HttpRequestMessage(HttpMethod.Get, "/change-password");
        form.Headers.Add("Cookie", cookie);
        var page = await client.SendAsync(form);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var post = new HttpRequestMessage(HttpMethod.Post, "/change-password")
        {
            Content = new FormUrlEncodedContent([
                new("__RequestVerificationToken", token),
                new("Input.CurrentPassword", StaffSeedData.Password),
                new("Input.NewPassword", "NewPassw0rd"),
                new("RepeatPassword", "OtherPassw0rd"),
            ]),
        };
        post.Headers.Add("Cookie", $"{cookie}; {UiTestClient.SetCookies(page)}");
        var response = await client.SendAsync(post);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The passwords do not match.", await response.Content.ReadAsStringAsync());
        Assert.False(LoginPage.SetsAuthCookie(response));
    }
}