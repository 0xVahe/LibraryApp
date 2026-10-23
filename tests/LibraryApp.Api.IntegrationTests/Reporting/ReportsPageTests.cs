using System.Net;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
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

    [Fact]
    public async Task Overdue_loans_have_filters_and_the_points_rule()
    {
        string html = await GetAsync(Librarian, Route);

        Assert.Contains("id=\"overdue-title\"", html);
        Assert.Contains("<option value=\"\">All departments</option>", html);
        Assert.Contains("Information Technology", html);
        Assert.Contains("<option value=\"\">Any working days overdue</option>", html);
        Assert.Contains("Points so far: 1 per working day overdue, max 10", html);
        Assert.Contains("action=\"/reports#overdue-title\"", html);
    }

    [Fact]
    public async Task Overdue_filter_keeps_the_choice()
    {
        var it = StaffSeedIds.Departments.InformationTechnology;

        string html = await GetAsync(Librarian, $"{Route}?departmentId={it}&days=6-10");

        Assert.Contains($"<option value=\"{it}\" selected=\"selected\">", html);
        Assert.Contains("<option value=\"6-10\" selected=\"selected\">", html);
    }

    [Fact]
    public async Task Unknown_filter_values_show_every_loan()
    {
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var response = await client.GetAsync($"{Route}?departmentId={Guid.NewGuid()}&days=abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("selected=\"selected\"", html);
    }

    [Fact]
    public async Task Top_books_and_categories_for_the_current_month()
    {
        string html = await GetAsync(Librarian, Route);

        Assert.Contains("id=\"period-title\"", html);
        Assert.Contains("Showing 1 ", html);
        Assert.Contains("id=\"books-title\"", html);
        Assert.Contains("id=\"categories-title\"", html);
        Assert.Contains("Top 10 · ", html);
        Assert.Contains("Top 5 · ", html);
    }

    [Fact]
    public async Task Chosen_period_is_shown_and_kept_by_the_overdue_filter()
    {
        var it = StaffSeedIds.Departments.InformationTechnology;

        string html = await GetAsync(Librarian, $"{Route}?departmentId={it}&from=2026-09-01&to=2026-09-30");

        Assert.Contains("Showing 1 Sep 2026 – 30 Sep 2026.", html);
        Assert.Contains("value=\"2026-09-01\"", html);
        Assert.Contains("<input type=\"hidden\" name=\"from\" value=\"2026-09-01\" />", html);
        Assert.Contains($"<input type=\"hidden\" name=\"departmentId\" value=\"{it}\" />", html);
    }

    [Fact]
    public async Task Reversed_period_shows_an_error_instead_of_the_tops()
    {
        string html = await GetAsync(Librarian, $"{Route}?from=2026-10-31&to=2026-10-01");

        Assert.Contains("id=\"period-error\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.DoesNotContain("id=\"books-title\"", html);
    }

    [Fact]
    public async Task Administrator_sees_reading_activity_for_every_department()
    {
        string html = await GetAsync(Admin, $"{Route}?from=2026-10-01&to=2026-10-31");

        Assert.Contains("id=\"reading-title\"", html);
        Assert.Contains("<th>Per 10 staff</th>", html);
        Assert.Contains("Every department is listed, also with zero loans · 1 Oct 2026 – 31 Oct 2026 · generated", html);
        string reading = html[html.IndexOf("id=\"reading-title\"", StringComparison.Ordinal)..html.IndexOf("id=\"more-title\"", StringComparison.Ordinal)];
        Assert.All(StaffSeedData.DepartmentList, department =>
            Assert.Contains($"<td data-label=\"Department\">{department.Name}</td>", reading));
    }

    [Fact]
    public async Task Administrator_can_open_every_other_report()
    {
        string html = await GetAsync(Admin, Route);

        Assert.Contains("id=\"more-title\"", html);
        Assert.Contains("href=\"/reports/inventory\"", html);
        Assert.Contains("href=\"/reports/penalties\"", html);
        Assert.Contains("href=\"/reports/purchase-candidates\"", html);
        Assert.DoesNotContain("Department-level figures are for the administrator only.", html);
    }

    [Fact]
    public async Task Librarian_sees_no_department_figures_and_only_the_inventory_report()
    {
        string html = await GetAsync(Librarian, Route);

        Assert.Contains("Department-level figures are for the administrator only.", html);
        Assert.DoesNotContain("id=\"reading-title\"", html);
        Assert.Contains("href=\"/reports/inventory\"", html);
        Assert.DoesNotContain("href=\"/reports/penalties\"", html);
        Assert.DoesNotContain("href=\"/reports/purchase-candidates\"", html);
    }

    [Fact]
    public async Task Reversed_period_hides_reading_activity_too()
    {
        string html = await GetAsync(Admin, $"{Route}?from=2026-10-31&to=2026-10-01");

        Assert.Contains("id=\"period-error\"", html);
        Assert.DoesNotContain("id=\"reading-title\"", html);
    }

    [Fact]
    public async Task Penalties_page_lists_every_department_for_the_period()
    {
        string html = await GetAsync(Admin, "/reports/penalties?from=2026-10-01&to=2026-10-31");

        Assert.Contains("id=\"penalties-title\"", html);
        Assert.Contains("<th>Issued</th><th>Waived</th><th>Active</th>", html);
        Assert.Contains("1 Oct 2026 – 31 Oct 2026 · generated", html);
        Assert.Contains("<a href=\"/reports\">Reports</a>", html);
        Assert.All(StaffSeedData.DepartmentList, department =>
            Assert.Contains($"<td data-label=\"Department\">{department.Name}</td>", html));
        Assert.DoesNotContain("style=\"", html);
    }

    [Fact]
    public async Task Penalties_page_shows_a_reversed_period_as_an_error()
    {
        string html = await GetAsync(Admin, "/reports/penalties?from=2026-10-31&to=2026-10-01");

        Assert.Contains("id=\"period-error\"", html);
        Assert.DoesNotContain("id=\"penalties-title\"", html);
    }

    [Fact]
    public async Task Librarian_cannot_open_the_penalties_page()
    {
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var response = await client.GetAsync("/reports/penalties");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData(Admin)]
    [InlineData(Librarian)]
    public async Task Inventory_page_lists_every_status_and_condition(string email)
    {
        string html = await GetAsync(email, "/reports/inventory");

        Assert.Contains("id=\"inventory-title\">All categories</h2>", html);
        Assert.Contains("<th>Status</th><th>Copies</th>", html);
        foreach (var condition in new[] { "New", "Good", "Fair", "Poor" })
            Assert.Contains($"<th>{condition}</th>", html);
        foreach (var status in new[] { "Available", "On loan", "Reserved", "Under repair", "Lost", "Withdrawn" })
            Assert.Contains($">{status}</span>", html);
        Assert.Contains("in the collection · ", html);
        Assert.DoesNotContain("style=\"", html);
    }

    [Fact]
    public async Task Inventory_filter_keeps_the_category_and_ignores_an_unknown_one()
    {
        var it = CatalogSeedIds.Category(3);

        string html = await GetAsync(Librarian, $"/reports/inventory?categoryId={it}");
        Assert.Contains($"<option value=\"{it}\" selected=\"selected\">", html);
        Assert.Contains("id=\"inventory-title\">IT &amp; Software Engineering</h2>", html);

        html = await GetAsync(Librarian, $"/reports/inventory?categoryId={Guid.NewGuid()}");
        Assert.Contains("id=\"inventory-title\">All categories</h2>", html);
    }

    [Fact]
    public async Task Purchase_candidates_page_lists_the_queued_books()
    {
        string html = await GetAsync(Admin, "/reports/purchase-candidates");

        Assert.Contains("id=\"candidates-title\"", html);
        Assert.Contains("<th>Waiting</th><th>Copies</th>", html);
        Assert.Contains($"href=\"/catalog/books/{CatalogSeedIds.Book(1)}\"", html);   // two Waiting reservations in the seed
        Assert.Contains("Top 10 · as of ", html);
    }

    [Fact]
    public async Task Librarian_cannot_open_the_purchase_candidates_page()
    {
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var response = await client.GetAsync("/reports/purchase-candidates");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Reports_have_export_csv_links_for_the_filters_and_period_shown()
    {
        var it = StaffSeedIds.Departments.InformationTechnology;

        string html = await GetAsync(Admin, $"{Route}?departmentId={it}&days=6-10&from=2026-10-01&to=2026-10-31");

        Assert.Contains($"href=\"/reports?handler=csv&amp;report=overdue-loans&amp;departmentId={it}&amp;days=6-10\">Export CSV</a>", html);
        Assert.Contains("href=\"/reports?handler=csv&amp;report=top-books-and-categories&amp;from=2026-10-01&amp;to=2026-10-31\">Export CSV</a>", html);
        Assert.Contains("href=\"/reports?handler=csv&amp;report=reading-activity-by-department&amp;from=2026-10-01&amp;to=2026-10-31\">Export CSV</a>", html);
    }

    [Theory]
    [InlineData(Librarian, "/reports?handler=csv&report=overdue-loans", "\"AsOfDate\",\"GeneratedAtUtc\",\"LoanId\"")]
    [InlineData(Librarian, "/reports?handler=csv&report=top-books-and-categories&from=2026-10-01&to=2026-10-31", "\"PeriodStart\",\"PeriodEnd\",\"GeneratedAtUtc\",\"Type\"")]
    [InlineData(Admin, "/reports?handler=csv&report=reading-activity-by-department&from=2026-10-01&to=2026-10-31", "\"Information Technology\"")]
    [InlineData(Admin, "/reports/penalties?handler=csv&from=2026-10-01&to=2026-10-31", "\"IssuedPoints\",\"WaivedPoints\",\"ActivePoints\"")]
    [InlineData(Librarian, "/reports/inventory?handler=csv", "\"Status\",\"StatusCopies\",\"Condition\"")]
    [InlineData(Admin, "/reports/purchase-candidates?handler=csv", "\"WaitingReservations\"")]
    public async Task Export_csv_downloads_the_report(string email, string path, string expected)
    {
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/reports?handler=csv&report=reading-activity-by-department")]
    [InlineData("/reports/penalties?handler=csv")]
    [InlineData("/reports/purchase-candidates?handler=csv")]
    public async Task Librarian_cannot_export_the_administrator_reports(string path)
    {
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Unknown_report_export_is_not_found()
    {
        using var client = await UiTestClient.SignedInAsync(factory, Librarian, StaffSeedData.Password);

        using var response = await client.GetAsync("/reports?handler=csv&report=unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_sees_the_charts_of_rep_02_to_rep_04()
    {
        string html = await GetAsync(Admin, $"{Route}?from=2026-10-01&to=2026-10-31");

        Assert.Contains("id=\"charts-title\"", html);
        foreach (var chart in new[] { "chart-loans", "chart-books", "chart-categories", "chart-departments" })
            Assert.Contains($"<ul class=\"chart\" aria-labelledby=\"{chart}\">", html);
        Assert.Contains("<svg class=\"chart-bar\" viewBox=\"0 0 100 12\" preserveAspectRatio=\"none\" aria-hidden=\"true\" focusable=\"false\">", html);
        Assert.Contains("class=\"chart-fill\" width=\"100\"", html);   // the largest value of a chart fills its bar
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=\"", html);
    }

    [Fact]
    public async Task Librarian_sees_no_charts()
    {
        string html = await GetAsync(Librarian, Route);

        Assert.DoesNotContain("id=\"charts-title\"", html);
    }

    [Fact]
    public async Task Reversed_period_keeps_only_the_dashboard_chart()
    {
        string html = await GetAsync(Admin, $"{Route}?from=2026-10-31&to=2026-10-01");

        Assert.Contains("aria-labelledby=\"chart-loans\"", html);
        Assert.DoesNotContain("id=\"chart-books\"", html);
        Assert.DoesNotContain("id=\"chart-departments\"", html);
    }

    private async Task<string> GetAsync(string email, string path)
    {
        using var client = await UiTestClient.SignedInAsync(factory, email, StaffSeedData.Password);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
