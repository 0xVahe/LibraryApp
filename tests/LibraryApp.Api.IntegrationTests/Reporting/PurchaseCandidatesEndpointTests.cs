using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Reporting.Options;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public async Task Administrator_gets_the_seeded_queue_with_the_book_details_and_the_limit()
    {
        // Borrowing's seed: book 1 has 2 Waiting reservations; book 15 has only a ReadyForPickup one (not a queue).
        int limit = factory.Services.GetRequiredService<IOptions<ReportingOptions>>().Value.PurchaseCandidatesLimit;

        using var client = CreateAuthenticatedClient(StaffRoles.LibraryAdministrator);
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        JsonElement root = json.RootElement;

        Assert.True(DateOnly.TryParse(root.GetProperty("asOfDate").GetString(), out _));
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        Assert.Equal(limit, root.GetProperty("limit").GetInt32());

        SeedBook seeded = CatalogSeedData.Books.Single(book => book.Number == 1);
        JsonElement book = Assert.Single(root.GetProperty("books").EnumerateArray());
        Assert.Equal(CatalogSeedIds.Book(1), book.GetProperty("bookId").GetGuid());
        Assert.Equal(seeded.Title, book.GetProperty("title").GetString());
        Assert.Equal(seeded.Isbn, book.GetProperty("isbn").GetString());
        Assert.Equal(
            CatalogSeedData.Categories.Single(category => category.Id == CatalogSeedIds.Category(seeded.CategoryNumber)).Name,
            book.GetProperty("category").GetString());
        Assert.Equal(2, book.GetProperty("waitingReservations").GetInt32());
        Assert.Equal(CatalogSeedData.CopiesPerBook[1], book.GetProperty("copiesInCollection").GetInt32());   // none lost or withdrawn
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
