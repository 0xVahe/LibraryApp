using System.Net;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;

namespace LibraryApp.Api.IntegrationTests.Reporting;

/// <summary>The Reports page (REP-F1): real UI cookies and the Development seed.</summary>
public class ReportsPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/reports";
    private const string Admin = "lilit.grigoryan@ameriabank.am";
    private const string Librarian = "aram.sargsyan@ameriabank.am";
    private const string Member = "anna.petrosyan@ameriabank.am";

    [Fact]
    public async Task Signed_out_goes_to_login()
    {
        using var client = UiTestClient.Anonymous(factory);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Staff_member_is_forbidden()
    {
        using var client = await UiTestClient.SignedInAsync(factory, Member, StaffSeedData.Password);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData(Admin)]
    [InlineData(Librarian)]
    public async Task Library_staff_see_the_dashboard(string email)
    {
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("aria-current=\"page\">Reports", html);
        Assert.Contains("id=\"dashboard-title\"", html);
        foreach (var stat in new[] { "books-stat", "readers-stat", "loans-stat", "overdue-stat", "restricted-stat", "suspended-stat" })
            Assert.Contains($"id=\"{stat}\"", html);
        Assert.Contains("refreshed every 5 minutes", html);
        Assert.DoesNotContain("style=\"", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }
}
