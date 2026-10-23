using System.Net;
using System.Net.Http.Headers;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Reporting;

public sealed class ReportingFormatEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    public static TheoryData<string, string> Reports => new()
    {
        { "overdue-loans", StaffRoles.Librarian },
        { "dashboard", StaffRoles.Librarian },
        { "top-books-and-categories?from=2026-01-01&to=2026-12-31", StaffRoles.Librarian },
        { "reading-activity-by-department?from=2026-01-01&to=2026-12-31", StaffRoles.LibraryAdministrator },
        { "penalties?from=2026-01-01&to=2026-12-31", StaffRoles.LibraryAdministrator },
        { "inventory-status", StaffRoles.Librarian },
        { "purchase-candidates", StaffRoles.LibraryAdministrator }
    };

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Csv_format_returns_csv(string report, string role)
    {
        using var client = CreateClient(role);
        using var response = await client.GetAsync(Route(report, "csv"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        string csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("GeneratedAtUtc", csv);
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Json_format_returns_json(string report, string role)
    {
        using var client = CreateClient(role);
        using var response = await client.GetAsync(Route(report, "json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Invalid_format_returns_400(string report, string role)
    {
        using var client = CreateClient(role);
        using var response = await client.GetAsync(Route(report, "xml"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = await System.Text.Json.JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        Assert.Equal("reporting.invalid_format",
            body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Anonymous_requests_return_401(string report, string _)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Route(report, "csv"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Staff_member_requests_return_403(string report, string _)
    {
        using var client = CreateClient(StaffRoles.StaffMember);
        using var response = await client.GetAsync(Route(report, "csv"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("reading-activity-by-department?from=2026-01-01&to=2026-12-31")]
    [InlineData("penalties?from=2026-01-01&to=2026-12-31")]
    [InlineData("purchase-candidates")]
    public async Task Librarian_cannot_export_admin_reports(string report)
    {
        using var client = CreateClient(StaffRoles.Librarian);
        using var response = await client.GetAsync(Route(report, "csv"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string Route(string report, string format) =>
        $"/api/reports/{report}{(report.Contains('?') ? '&' : '?')}format={format}";

    private HttpClient CreateClient(string role)
    {
        var client = factory.CreateClient();

        string token = factory.Services
            .GetRequiredService<IAccessTokenIssuer>()
            .Issue(factory.Account(role))
            .Token;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
