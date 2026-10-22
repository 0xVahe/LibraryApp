using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Reporting;

/// <summary>REP-05 over HTTP, on the Development seed.</summary>
public sealed class InventoryStatusEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/reports/inventory-status";

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_staff_member_gets_403()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.StaffMember);
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Librarian_and_administrator_get_every_status_and_condition_by_name(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        JsonElement root = json.RootElement;

        Assert.True(root.GetProperty("totalCopies").GetInt32() > 0);
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        List<JsonElement> statuses = root.GetProperty("statuses").EnumerateArray().ToList();
        Assert.Equal(Enum.GetNames<BookCopyStatus>(), statuses.Select(status => status.GetProperty("status").GetString()));
        Assert.All(statuses, status => Assert.Equal(
            Enum.GetNames<BookCopyCondition>(),
            status.GetProperty("conditions").EnumerateArray().Select(condition => condition.GetProperty("condition").GetString())));
    }

    [Fact]
    public async Task A_seeded_category_is_named_in_the_report()
    {
        (Guid id, string name, _) = CatalogSeedData.Categories[0];
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync($"{Route}?categoryId={id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        Assert.Equal(id, json.RootElement.GetProperty("categoryId").GetGuid());
        Assert.Equal(name, json.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public async Task An_unknown_category_returns_404_with_the_error_code()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync($"{Route}?categoryId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        Assert.Equal("reporting.category_not_found", json.RootElement.GetProperty("code").GetString());
    }

    private HttpClient CreateAuthenticatedClient(string role)
    {
        var client = factory.CreateClient();

        var token = factory.Services
            .GetRequiredService<IAccessTokenIssuer>()
            .Issue(factory.Account(role))
            .Token;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
