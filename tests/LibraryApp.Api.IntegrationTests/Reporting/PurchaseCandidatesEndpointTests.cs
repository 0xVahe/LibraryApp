using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Reporting;

/// <summary>REP-07 over HTTP, on the Development seed.</summary>
public sealed class PurchaseCandidatesEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/reports/purchase-candidates";

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember)]
    [InlineData(StaffRoles.Librarian)]   // REP-07: purchase decisions are for the administrator only
    public async Task Other_roles_than_the_administrator_get_403(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_gets_the_books_longest_queue_first_within_the_limit()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.LibraryAdministrator);
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        JsonElement root = json.RootElement;

        Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        List<int> queues = root.GetProperty("books").EnumerateArray()
            .Select(book => book.GetProperty("waitingReservations").GetInt32())
            .ToList();
        Assert.InRange(queues.Count, 0, 10);   // Reporting:PurchaseCandidatesLimit
        Assert.All(queues, queue => Assert.True(queue > 0));
        Assert.Equal(queues.OrderByDescending(queue => queue), queues);
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
