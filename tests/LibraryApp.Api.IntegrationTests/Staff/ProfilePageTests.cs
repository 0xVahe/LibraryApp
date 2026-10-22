using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class ProfilePageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly string StaffMemberEmail = LoginPage.Email(StaffSeedIds.StaffMembers.AnnaPetrosyan);
    private static readonly string LibrarianEmail = LoginPage.Email(StaffSeedIds.StaffMembers.AramSargsyan);

    [Fact]
    public async Task A_staff_member_sees_their_editable_name_and_the_password_form()
    {
        var client = await UiTestClient.SignedInAsync(factory, StaffMemberEmail, StaffSeedData.Password);

        var response = await client.GetAsync("/profile");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.FirstName\"", html);
        Assert.Contains("value=\"Anna\"", html);
        Assert.Contains("anna.petrosyan@ameriabank.am", html);
        Assert.Contains("action=\"/change-password\"", html);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task A_librarian_sees_a_read_only_name_and_a_link_to_their_staff_page()
    {
        var client = await UiTestClient.SignedInAsync(factory, LibrarianEmail, StaffSeedData.Password);

        var html = await (await client.GetAsync("/profile")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("name=\"Input.FirstName\"", html);
        Assert.Contains($"/staff/{StaffSeedIds.StaffMembers.AramSargsyan}/edit", html);
    }

    [Fact]
    public async Task An_empty_first_name_is_shown_on_the_form()
    {
        var response = await PostAsync("", "Petrosyan");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("aria-invalid=\"true\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Saving_the_name_returns_to_the_profile()
    {
        var response = await PostAsync("Anna", "Petrosyan");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/profile", response.Headers.Location!.OriginalString);
    }

    private async Task<HttpResponseMessage> PostAsync(string firstName, string lastName)
    {
        var client = UiTestClient.Anonymous(factory);
        var cookie = await UiTestClient.CookieAsync(factory, StaffMemberEmail, StaffSeedData.Password);
        var form = new HttpRequestMessage(HttpMethod.Get, "/profile");
        form.Headers.Add("Cookie", cookie);
        var page = await client.SendAsync(form);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var post = new HttpRequestMessage(HttpMethod.Post, "/profile")
        {
            Content = new FormUrlEncodedContent([
                new("__RequestVerificationToken", token),
                new("Input.FirstName", firstName),
                new("Input.LastName", lastName),
            ]),
        };
        post.Headers.Add("Cookie", $"{cookie}; {UiTestClient.SetCookies(page)}");
        return await client.SendAsync(post);
    }
}