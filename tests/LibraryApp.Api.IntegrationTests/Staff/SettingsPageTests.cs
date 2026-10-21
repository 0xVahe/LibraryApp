using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class SettingsPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Path = "/settings";

    [Fact]
    public async Task Administrator_sees_department_and_category_settings()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Departments", html);
        Assert.Contains("Staff categories", html);
        Assert.Contains("Information Technology", html);
        Assert.Contains("Employee", html);
        Assert.Contains("Maximum books", html);
        Assert.Contains("Loan days", html);
        Assert.Contains("Maximum renewals", html);
        Assert.DoesNotContain("Delete department", html);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task Librarian_gets_forbidden_from_settings()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        using var forbidden = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Adding_a_duplicate_department_shows_a_conflict_error_and_preserves_the_name()
    {
        using var client = await ClientAsync(StaffSeedIds.StaffMembers.LilitGrigoryan);
        using var page = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = Regex.Match(await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        using var scope = factory.Services.CreateScope();
        var existing = (await scope.ServiceProvider.GetRequiredService<IDepartmentService>().ListAsync()).First();
        var fields = new Dictionary<string, string>
        {
            ["Name"] = existing.Name,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Path + "?handler=CreateDepartment")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", string.Join("; ",
            client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(page));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("A department with this name already exists.", html);
        Assert.Contains("alert-red", html);
        Assert.Contains($"value=\"{existing.Name}\"", html);
        Assert.DoesNotContain("Department added.", html);
    }

    private Task<HttpClient> ClientAsync(Guid staffMemberId)
    {
        var person = StaffSeedData.People.Single(item => item.StaffMemberId == staffMemberId);
        return UiTestClient.SignedInAsync(factory, person.Email, StaffSeedData.Password);
    }
}
