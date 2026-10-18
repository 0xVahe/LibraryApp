using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Reporting;

/// <summary>REP-04 over HTTP, on the Development seed.</summary>
public sealed class ReadingActivityEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/reports/reading-activity-by-department";
    private const string October = Route + "?from=2026-10-01&to=2026-10-31";

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(October);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember)]
    [InlineData(StaffRoles.Librarian)]   // REP-F5: department-level figures are for the administrator only
    public async Task Other_roles_than_the_administrator_get_403(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync(October);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_gets_every_seeded_department_with_the_period()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.LibraryAdministrator);
        using var response = await client.GetAsync(October);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        JsonElement root = json.RootElement;

        Assert.Equal("2026-10-01", root.GetProperty("periodStart").GetString());
        Assert.Equal("2026-10-31", root.GetProperty("periodEnd").GetString());
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));

        List<string?> departments = root.GetProperty("departments").EnumerateArray()
            .Select(department => department.GetProperty("department").GetString())
            .ToList();
        Assert.All(StaffSeedData.DepartmentList, seeded => Assert.Contains(seeded.Name, departments));
    }

    [Theory]
    [InlineData(Route + "?from=2026-10-31&to=2026-10-01")]
    [InlineData(Route + "?to=2026-10-31")]
    public async Task An_invalid_date_range_returns_400_with_the_error_code(string route)
    {
        using var client = CreateAuthenticatedClient(StaffRoles.LibraryAdministrator);
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        Assert.Equal("reporting.invalid_date_range", json.RootElement.GetProperty("code").GetString());
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