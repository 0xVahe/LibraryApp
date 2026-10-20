using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>S2-07: real cookie authentication, Razor forms, application services and SQL Server.</summary>
public class StaffMemberPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static string Path(Guid id) => $"/staff/{id}";

    private Task<HttpClient> Client(bool administrator = false) => UiTestClient.SignedInAsync(factory,
        administrator ? "lilit.grigoryan@ameriabank.am" : "aram.sargsyan@ameriabank.am", StaffSeedData.Password);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Library_staff_can_view_search_and_private_profile_details(bool administrator)
    {
        using var client = await Client(administrator);
        using var list = await client.GetAsync("/staff");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        var listHtml = await list.Content.ReadAsStringAsync();
        Assert.Contains("/staff/import", listHtml);
        Assert.Contains("/staff/new", listHtml);
        Assert.Contains("Information Technology", listHtml);
        Assert.Contains("aria-current=\"page\"", listHtml);

        var member = await Profile(Members.AnnaPetrosyan);
        using var details = await client.GetAsync(Path(member.Id));
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.True(details.Headers.CacheControl?.NoStore);
        var html = WebUtility.HtmlDecode(await details.Content.ReadAsStringAsync());
        foreach (var text in new[] { member.EmployeeId, member.CorporateEmail, member.DepartmentName,
                     member.Position, member.StaffCategoryName, member.LineManagerEmail! })
            Assert.Contains(text, html);
        Assert.Contains($"href=\"/staff/{member.Id}/edit\"", html);
        Assert.Contains("Borrowing details are currently unavailable.", html);
        Assert.DoesNotContain("class=\"owner", html);
        Assert.DoesNotContain("style=\"", html);
    }

    [Theory]
    [InlineData("Anna")]
    [InlineData("Petrosyan")]
    [InlineData("Anna Pet")]
    [InlineData("EMP-10001")]
    public async Task Search_by_name_or_employee_id_shows_only_matching_staff(string search)
    {
        using var client = await Client();
        var html = await client.GetStringAsync($"/staff?search={Uri.EscapeDataString(search)}");
        var table = StaffTable(html);
        Assert.Contains("Anna Petrosyan", table);
        Assert.DoesNotContain("Davit Hakobyan", table);
        Assert.Single(Regex.Matches(table, "data-label=\"Employee ID\""));
    }

    [Fact]
    public async Task The_join_date_uses_the_library_time_zone_instead_of_the_utc_calendar_date()
    {
        var member = await Enrol();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var joinedUtc = new DateTime(2026, 10, 18, 22, 0, 0, DateTimeKind.Utc);
            await db.StaffMembers.Where(x => x.Id == member.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.JoinedLibraryAtUtc, joinedUtc));
        }
        using var client = await Client();
        var html = await client.GetStringAsync(Path(member.Id));
        Assert.Contains("datetime=\"2026-10-19\"", html);
        Assert.Contains("19 Oct 2026", html);
    }

    [Fact]
    public async Task Search_department_and_status_filters_are_combined()
    {
        var member = await Profile(Members.GorMkrtchyan);
        using var client = await Client();
        var query = $"/staff?search=Gor&departmentId={member.DepartmentId}&status=Restricted";
        var html = await client.GetStringAsync(query);
        Assert.Contains(member.EmployeeId, StaffTable(html));
        Assert.Single(Regex.Matches(StaffTable(html), "data-label=\"Employee ID\""));
        Assert.Matches($"<option[^>]*value=\"{member.DepartmentId}\"[^>]*selected", html);
        Assert.Matches("<option[^>]*value=\"Restricted\"[^>]*selected", html);

        var noMatch = await client.GetStringAsync(query.Replace("status=Restricted", "status=Active"));
        Assert.Contains("No staff members match your filters.", noMatch);
        Assert.DoesNotContain("<table", noMatch);
    }

    [Fact]
    public async Task Pagination_preserves_filters_and_changes_the_visible_members()
    {
        using var client = await Client();
        var first = WebUtility.HtmlDecode(await client.GetStringAsync("/staff?search=EMP-&status=Active"));
        var pager = Regex.Match(first, "<nav class=\"pager\"[\\s\\S]*?</nav>").Value;
        var next = Regex.Match(pager, "href=\"([^\"]*page=2[^\"]*)\"").Groups[1].Value;
        Assert.NotEmpty(next);
        Assert.Contains("search=EMP-", next);
        Assert.Contains("status=Active", next);
        Assert.Equal(20, Regex.Matches(StaffTable(first), "data-label=\"Employee ID\"").Count);

        var second = await client.GetStringAsync(next);
        var firstIds = Regex.Matches(StaffTable(first), "href=\"/staff/([a-f0-9-]+)\"")
            .Select(match => match.Groups[1].Value).ToHashSet();
        var secondIds = Regex.Matches(StaffTable(second), "href=\"/staff/([a-f0-9-]+)\"")
            .Select(match => match.Groups[1].Value).ToHashSet();
        Assert.NotEmpty(secondIds);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.DoesNotContain("name=\"page\"", first); // changing filters starts at the first page
    }

    [Theory]
    [InlineData("search=NobodyMatchesThis")]
    [InlineData("page=2147483647")]
    public async Task Empty_results_have_a_clear_recovery_link(string query)
    {
        using var client = await Client();
        var html = await client.GetStringAsync($"/staff?{query}");
        Assert.Contains("No staff members match your filters.", html);
        Assert.Contains(">View all staff members</a>", html);
    }

    [Theory]
    [InlineData("departmentId=not-a-guid", "staff-department")]
    [InlineData("status=Unknown", "staff-status")]
    public async Task Invalid_filters_show_an_accessible_error_instead_of_an_error_page(string query, string control)
    {
        using var client = await Client();
        using var response = await client.GetAsync($"/staff?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("alert alert-red", html);
        Assert.Matches($"<select[^>]*id=\"{control}\"[^>]*aria-invalid=\"true\"", html);
    }

    [Fact]
    public async Task Search_text_is_html_encoded()
    {
        const string input = "\"><script>alert(1)</script>";
        using var client = await Client();
        var html = await client.GetStringAsync($"/staff?search={Uri.EscapeDataString(input)}");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outstanding_books_are_shown_and_deactivation_changes_nothing(bool administrator)
    {
        var id = Members.AnnaPetrosyan;
        var before = await State(id);
        var books = await OutstandingBooks(id);
        Assert.NotEmpty(books);
        using var client = await Client(administrator);
        using var response = await Deactivate(client, id, "Employee left the bank.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("alert alert-red", html);
        Assert.Contains("Return all outstanding books", html);
        Assert.Contains("Employee left the bank.", html);
        foreach (var book in books)
        {
            Assert.Contains(book.Title, html);
            Assert.Contains(book.Barcode, html);
        }
        Assert.Equal(before, await State(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task A_reason_is_required_and_invalid_input_writes_nothing(string reason)
    {
        var member = await Enrol();
        var before = await State(member.Id);
        using var client = await Client();
        using var response = await Deactivate(client, member.Id, reason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("<textarea[^>]*aria-invalid=\"true\"", html);
        Assert.Contains("data-valmsg-for=\"Input.Reason\"", html);
        Assert.Equal(before, await State(member.Id));
    }

    [Fact]
    public async Task An_overlong_reason_is_preserved_with_its_field_error_and_writes_nothing()
    {
        var member = await Enrol();
        var before = await State(member.Id);
        var reason = new string('x', 1001);
        using var client = await Client();
        using var response = await Deactivate(client, member.Id, reason);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1000 characters or fewer", html);
        Assert.Contains(reason, html);
        Assert.Matches("<textarea[^>]*aria-invalid=\"true\"", html);
        Assert.Equal(before, await State(member.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_deactivation_redirects_to_the_updated_profile_with_a_green_alert(bool administrator)
    {
        var member = await Enrol();
        var before = await State(member.Id);
        using var client = await Client(administrator);
        using var response = await Deactivate(client, member.Id, "  Employee left the bank.  ");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Path(member.Id), response.Headers.Location!.OriginalString);
        KeepCookies(client, response);
        var html = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Staff member deactivated.", html);
        Assert.Contains("alert-green", html);
        Assert.Contains("Account deactivated", html);
        Assert.DoesNotContain("name=\"Input.Reason\"", html);
        Assert.DoesNotContain($"href=\"/staff/{member.Id}/edit\"", html);
        var after = await State(member.Id);
        Assert.Equal(nameof(StaffMemberStatus.Deactivated), after.Profile.Status);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.True(after.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, after.LockoutEnd);
        Assert.Equal(before.Events + 1, after.Events);
    }

    [Fact]
    public async Task A_second_post_cannot_deactivate_the_account_twice()
    {
        var member = await Enrol();
        using var client = await Client();
        var token = await Token(client, member.Id);
        var fields = new Dictionary<string, string> { ["Input.Reason"] = "Left the bank", ["__RequestVerificationToken"] = token };
        using var first = await client.PostAsync(Path(member.Id), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var before = await State(member.Id);
        using var second = await client.PostAsync(Path(member.Id), new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Account deactivated", await second.Content.ReadAsStringAsync());
        Assert.Equal(before, await State(member.Id));
    }

    [Fact]
    public async Task Deactivation_without_an_antiforgery_token_is_rejected_without_writes()
    {
        var member = await Enrol();
        var before = await State(member.Id);
        using var client = await Client();
        using var response = await client.PostAsync(Path(member.Id), new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Input.Reason"] = "Left the bank" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await State(member.Id));
    }

    [Fact]
    public async Task A_staff_member_cannot_view_private_staff_data_or_deactivate_anyone()
    {
        var member = await Enrol();
        var before = await State(member.Id);
        using var client = await UiTestClient.SignedInAsync(factory, "anna.petrosyan@ameriabank.am", StaffSeedData.Password);
        foreach (var path in new[] { "/staff", Path(member.Id) })
        {
            using var response = await client.GetAsync(path);
            await AssertForbidden(client, response);
            Assert.DoesNotContain(member.CorporateEmail, await response.Content.ReadAsStringAsync());
        }
        using var post = await client.PostAsync(Path(member.Id), new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Input.Reason"] = "Tampered request" }));
        await AssertForbidden(client, post);
        Assert.Equal(before, await State(member.Id));
    }

    [Theory]
    [InlineData("/staff")]
    [InlineData("/staff/20000000-0000-0000-0003-000000000003")]
    public async Task Anonymous_visitors_are_sent_to_login(string path)
    {
        using var client = UiTestClient.Anonymous(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
    }

    [Theory]
    [InlineData("/staff/not-a-guid")]
    [InlineData("/staff/00000000-0000-0000-0000-000000000001")]
    public async Task Unknown_staff_profiles_show_the_not_found_page(string path)
    {
        using var client = await Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private static string StaffTable(string html) => Regex.Match(html, "<table[\\s\\S]*?</table>").Value;

    private async Task<StaffMemberDto> Profile(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().GetAsync(id);
    }

    private async Task<StaffMemberDto> Enrol()
    {
        var unique = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().EnrolAsync(
            new EnrolStaffMemberRequest($"UI-{unique}", "Demo", "Employee", $"ui.{unique}@ameriabank.am",
                StaffSeedIds.Departments.InformationTechnology, "Engineer", null, StaffSeedIds.Categories.Employee, StaffSeedData.Password));
    }

    private sealed record StoredState(StaffMemberDto Profile, bool LockoutEnabled, DateTimeOffset? LockoutEnd, string? SecurityStamp, int Events);

    private async Task<StoredState> State(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var member = await db.StaffMembers.AsNoTracking().SingleAsync(x => x.Id == id);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == member.UserId);
        return new(await scope.ServiceProvider.GetRequiredService<IStaffManagementService>().GetAsync(id),
            user.LockoutEnabled, user.LockoutEnd, user.SecurityStamp, await db.OutboxMessages.CountAsync());
    }

    private async Task<IReadOnlyList<OutstandingBookDto>> OutstandingBooks(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var loans = await scope.ServiceProvider.GetRequiredService<IBorrowingReadService>().GetActiveLoansAsync(id);
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var copies = (await catalog.GetCopiesAsync(loans.Select(x => x.BookCopyId).ToArray())).ToDictionary(x => x.Id);
        var books = new List<OutstandingBookDto>();
        foreach (var loan in loans)
            books.Add(new((await catalog.GetBookAsync(loan.BookId))!.Title, copies[loan.BookCopyId].Barcode));
        return books;
    }

    private async Task<HttpResponseMessage> Deactivate(HttpClient client, Guid id, string reason) =>
        await client.PostAsync(Path(id), new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Input.Reason"] = reason, ["__RequestVerificationToken"] = await Token(client, id) }));

    private static async Task<string> Token(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Path(id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        KeepCookies(client, response);
        var token = Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return WebUtility.HtmlDecode(token);
    }

    private static void KeepCookies(HttpClient client, HttpResponseMessage response)
    {
        var existing = client.DefaultRequestHeaders.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";
        var cookies = (existing + "; " + UiTestClient.SetCookies(response)).Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Trim().Split('=', 2)).Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0]).Select(group => group.Last());
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(pair => $"{pair[0]}={pair[1]}")));
    }

    private static async Task AssertForbidden(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var target = new Uri(client.BaseAddress!, response.Headers.Location!);
        Assert.Equal("/forbidden", target.AbsolutePath);
        using var forbidden = await client.GetAsync(target.PathAndQuery);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}

/// <summary>Proves the page can consume Group 3's component when it is registered, without owning its implementation.</summary>
public sealed class StaffBorrowingSlotFactory : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddControllers().AddApplicationPart(typeof(TestStaffBorrowingSummaryViewComponent).Assembly);
}

[ViewComponent(Name = "StaffBorrowingSummary")]
public sealed class TestStaffBorrowingSummaryViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(Guid staffMemberId) => Content($"Borrowing summary for {staffMemberId}");
}

public class StaffBorrowingSlotTests(StaffBorrowingSlotFactory factory) : IClassFixture<StaffBorrowingSlotFactory>
{
    [Fact]
    public async Task The_registered_component_receives_the_viewed_staff_id()
    {
        using var client = await UiTestClient.SignedInAsync(factory, "aram.sargsyan@ameriabank.am", StaffSeedData.Password);
        var html = await client.GetStringAsync($"/staff/{Members.AnnaPetrosyan}");
        Assert.Contains($"Borrowing summary for {Members.AnnaPetrosyan}", html);
        Assert.DoesNotContain("Borrowing details are currently unavailable.", html);
    }
}
