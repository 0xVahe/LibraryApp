using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>MEM-08: real UI cookies, SQL Server and the merged Borrowing components (NFR-03).</summary>
public class MyLibraryPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/my-library";

    [Theory]
    [InlineData("anna.petrosyan@ameriabank.am")]
    [InlineData("aram.sargsyan@ameriabank.am")]
    [InlineData("lilit.grigoryan@ameriabank.am")]
    [InlineData("gor.mkrtchyan@ameriabank.am")]
    [InlineData("sona.vardanyan@ameriabank.am")]
    public async Task Every_role_including_restricted_and_suspended_members_sees_its_own_seeded_summary(string email)
    {
        var person = StaffSeedData.People.Single(x => x.Email == email);
        using var scope = factory.Services.CreateScope();
        var expected = await scope.ServiceProvider.GetRequiredService<IBorrowingReadService>()
            .GetStaffSummaryAsync(person.StaffMemberId);
        var profile = await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().GetAsync(person.StaffMemberId);
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected.ActiveLoans.Count.ToString(), Stat(html, "my-loans-stat"));
        Assert.Equal(expected.Reservations.Count.ToString(), Stat(html, "my-reservations-stat"));
        Assert.Equal(expected.ActivePenaltyPoints.ToString(), Stat(html, "my-penalties-stat"));
        Assert.Equal(profile.Status, Stat(html, "my-status-stat"));
        Assert.Contains($"Hello, {profile.FirstName}", WebUtility.HtmlDecode(html));
        var card = WebUtility.HtmlDecode(ProfileCard(html));
        foreach (var field in new[] { profile.FirstName, profile.LastName, profile.CorporateEmail,
                     profile.DepartmentName, profile.Position, profile.StaffCategoryName })
            Assert.Contains(field, card);
        Assert.DoesNotContain("Line manager", card);
        Assert.Contains("href=\"/profile\"", card);
        Assert.Contains("href=\"/catalog\"", html);
        Assert.Contains("aria-current=\"page\">My library", html);
        Assert.Contains("id=\"my-loans-title\"", html);
        Assert.Contains("id=\"my-reservations-title\"", html);
        Assert.Contains("id=\"my-penalties-title\"", html);
        Assert.DoesNotContain("class=\"owner", html);
        Assert.DoesNotContain("style=\"", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signed_out_users_are_redirected_to_login_with_a_return_url()
    {
        using var client = UiTestClient.Anonymous(factory);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = new Uri(client.BaseAddress!, response.Headers.Location!);
        Assert.Equal("/login", location.AbsolutePath);
        Assert.Contains(Route, Uri.UnescapeDataString(location.Query));
    }

    [Fact]
    public async Task A_requested_member_id_cannot_replace_the_caller_or_leak_into_the_next_request()
    {
        var anna = StaffSeedData.People.Single(x => x.StaffMemberId == StaffSeedIds.StaffMembers.AnnaPetrosyan);
        var narek = StaffSeedData.People.Single(x => x.StaffMemberId == StaffSeedIds.StaffMembers.NarekSimonyan);
        using var first = await UiTestClient.SignedInAsync(factory, anna.Email, StaffSeedData.Password);
        using var second = await UiTestClient.SignedInAsync(factory, narek.Email, StaffSeedData.Password);

        var firstHtml = await first.GetStringAsync($"{Route}?staffMemberId={narek.StaffMemberId}&id={narek.StaffMemberId}");
        var secondHtml = await second.GetStringAsync($"{Route}?staffMemberId={anna.StaffMemberId}");

        Assert.Contains(anna.Email, ProfileCard(firstHtml));
        Assert.DoesNotContain(narek.Email, ProfileCard(firstHtml));
        Assert.Contains(narek.Email, ProfileCard(secondHtml));
        Assert.DoesNotContain(anna.Email, ProfileCard(secondHtml));
    }

    [Fact]
    public async Task A_member_without_borrowing_activity_sees_zero_stats_and_real_empty_states()
    {
        using var scope = factory.Services.CreateScope();
        var unique = Guid.NewGuid().ToString("N");
        var member = await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().EnrolAsync(
            new EnrolStaffMemberRequest($"MY-{unique[..12]}", "Demo", "Employee", $"my.{unique}@ameriabank.am",
                StaffSeedIds.Departments.InformationTechnology, "Engineer", null,
                StaffSeedIds.Categories.Employee, StaffSeedData.Password));
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var userId = await db.StaffMembers.Where(x => x.Id == member.Id).Select(x => x.UserId).SingleAsync();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
            .ChangePasswordAsync(userId, StaffSeedData.Password, "ChangedPass9", default));
        using var client = await UiTestClient.SignedInAsync(factory, member.CorporateEmail, "ChangedPass9");

        var html = await client.GetStringAsync(Route);

        Assert.Equal("0", Stat(html, "my-loans-stat"));
        Assert.Equal("0", Stat(html, "my-reservations-stat"));
        Assert.Equal("0", Stat(html, "my-penalties-stat"));
        Assert.Contains("No books on loan.", html);
        Assert.Contains("No reservations.", html);
        Assert.Contains("No penalty points.", html);
        Assert.DoesNotContain("book is overdue", html);
        Assert.Contains("Latest notifications are currently unavailable.", html);
    }

    [Fact]
    public async Task The_greeting_and_profile_encode_stored_names()
    {
        var id = StaffSeedIds.StaffMembers.AnnaPetrosyan;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var member = await db.StaffMembers.SingleAsync(x => x.Id == id);
        var original = member.FirstName;
        const string script = "<script>alert('name')</script>";
        try
        {
            member.UpdateProfile(script, member.LastName, member.CorporateEmail, member.Position, member.LineManagerEmail);
            await db.SaveChangesAsync();
            using var client = await UiTestClient.SignedInAsync(factory, member.CorporateEmail, StaffSeedData.Password);

            var html = await client.GetStringAsync(Route);

            Assert.DoesNotContain(script, html);
            Assert.Contains("&lt;script&gt;", ProfileCard(html));
            Assert.Contains(script, WebUtility.HtmlDecode(ProfileCard(html)));
        }
        finally
        {
            member.UpdateProfile(original, member.LastName, member.CorporateEmail, member.Position, member.LineManagerEmail);
            await db.SaveChangesAsync();
        }
    }

    private static string Stat(string html, string id)
    {
        var card = Regex.Match(html, $"<div[^>]*id=\"{id}\"[^>]*>([\\s\\S]*?)</div>").Groups[1].Value;
        Assert.NotEmpty(card);
        var value = Regex.Match(card, "<span class=\"stat-value\">([\\s\\S]*)").Groups[1].Value;
        return WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]*>", "")).Trim();
    }

    private static string ProfileCard(string html) =>
        Regex.Match(html, "<section[^>]*aria-labelledby=\"my-profile-title\"[\\s\\S]*?</section>").Value;
}
