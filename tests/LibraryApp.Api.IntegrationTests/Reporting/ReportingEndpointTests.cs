
using System.Net;
using System.Net.Http.Headers;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Reporting;

public sealed class ReportingEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private static readonly string[] Routes =
    [
        "/api/reports/overdue-loans",
        "/api/reports/dashboard",
        "/api/reports/top-books-and-categories?from=2026-10-01&to=2026-10-16"
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Anonymous_requests_return_401(int routeIndex)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Routes[routeIndex]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Staff_member_requests_return_403(int routeIndex)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.StaffMember);
        using var response = await client.GetAsync(Routes[routeIndex]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/overdue-loans?minimumDaysOverdue=abc")]
    [InlineData("/api/reports/overdue-loans?departmentId=invalid")]
    [InlineData("/api/reports/top-books-and-categories?from=invalid&to=2026-10-16")]
    [InlineData("/api/reports/top-books-and-categories?from=2026-10-01&to=invalid")]
    public async Task Invalid_query_types_return_400(string route)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    
    [Theory]
    [InlineData("/api/reports/overdue-loans?minimumDaysOverdue=-1")]
    [InlineData("/api/reports/overdue-loans?maximumDaysOverdue=-1")]
    [InlineData("/api/reports/overdue-loans?minimumDaysOverdue=10&maximumDaysOverdue=5")]
    [InlineData("/api/reports/overdue-loans?departmentId=00000000-0000-0000-0000-000000000000")]
    [InlineData("/api/reports/top-books-and-categories?from=2026-10-16&to=2026-10-01")]
    [InlineData("/api/reports/top-books-and-categories?from=0001-01-01&to=2026-10-16")]
    public async Task Invalid_business_filters_return_400(string route)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Authorized_roles_can_get_dashboard(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync("/api/reports/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Authorized_roles_can_get_overdue_report(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync(
            "/api/reports/overdue-loans?minimumDaysOverdue=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Authorized_roles_can_get_top_books_and_categories(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync(
            "/api/reports/top-books-and-categories?from=2026-01-01&to=2026-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }


    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = factory.CreateClient();

        var token = factory.Services
            .GetRequiredService<IAccessTokenIssuer>()
            .Issue(new LoginAccount(
                Guid.NewGuid(),
                "reporting-tests@example.com",
                Guid.NewGuid(),
                StaffMemberStatus.Active,
                [role]))
            .Token;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
