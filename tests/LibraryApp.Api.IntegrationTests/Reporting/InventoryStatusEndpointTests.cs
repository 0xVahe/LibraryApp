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
    [InlineData(StaffRoles.LibraryAdministrator)]   // REP-F1: every report is open to both
    public async Task Librarian_and_administrator_get_every_seeded_copy_by_status_and_condition(string role)
    {
        using var client = CreateAuthenticatedClient(role);
        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        JsonElement root = json.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("categoryId").ValueKind);
        Assert.True(DateOnly.TryParse(root.GetProperty("asOfDate").GetString(), out _));
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));

        // The seed creates every copy in Good condition; Borrowing's seed loses one (book 15, copy 3).
        int seededCopies = CatalogSeedData.CopiesPerBook.Values.Sum();
        Assert.Equal(seededCopies, root.GetProperty("totalCopies").GetInt32());
        List<JsonElement> statuses = root.GetProperty("statuses").EnumerateArray().ToList();
        Assert.Equal(Enum.GetNames<BookCopyStatus>(), statuses.Select(status => status.GetProperty("status").GetString()));
        Assert.Equal(seededCopies, statuses.Sum(status => status.GetProperty("copies").GetInt32()));
        Assert.Equal(1, CopiesOf(statuses, nameof(BookCopyStatus.Lost)));
        Assert.Equal(0, CopiesOf(statuses, nameof(BookCopyStatus.Withdrawn)));
        Assert.Equal(seededCopies - 1, root.GetProperty("copiesInCollection").GetInt32());
        Assert.All(statuses, status =>
        {
            List<JsonElement> conditions = status.GetProperty("conditions").EnumerateArray().ToList();
            Assert.Equal(Enum.GetNames<BookCopyCondition>(), conditions.Select(condition => condition.GetProperty("condition").GetString()));
            Assert.Equal(status.GetProperty("copies").GetInt32(), conditions.Sum(condition => condition.GetProperty("copies").GetInt32()));
            Assert.All(
                conditions.Where(condition => condition.GetProperty("condition").GetString() != nameof(BookCopyCondition.Good)),
                condition => Assert.Equal(0, condition.GetProperty("copies").GetInt32()));
        });
    }

    [Fact]
    public async Task A_category_counts_the_copies_of_its_books_and_of_its_subcategories()
    {
        // Banking & Finance (1) with its subcategories Investing & Markets (9) and Banking & Financial History (10).
        (Guid id, string name, _) = CatalogSeedData.Categories[0];
        HashSet<Guid> categories = CatalogSeedData.Categories
            .Where(category => category.Id == id || category.ParentId == id)
            .Select(category => category.Id)
            .ToHashSet();
        int expected = CatalogSeedData.Books
            .Where(book => categories.Contains(CatalogSeedIds.Category(book.CategoryNumber)))
            .Sum(book => CatalogSeedData.CopiesPerBook[book.Number]);
        Assert.True(categories.Count > 1);

        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync($"{Route}?categoryId={id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        Assert.Equal(id, json.RootElement.GetProperty("categoryId").GetGuid());
        Assert.Equal(name, json.RootElement.GetProperty("category").GetString());
        Assert.Equal(expected, json.RootElement.GetProperty("totalCopies").GetInt32());
    }

    [Fact]
    public async Task An_empty_category_id_returns_400_with_the_error_code()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync($"{Route}?categoryId={Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(json);
        Assert.Equal("reporting.invalid_category_id", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_category_id_that_is_not_a_guid_returns_400()
    {
        using var client = CreateAuthenticatedClient(StaffRoles.Librarian);
        using var response = await client.GetAsync($"{Route}?categoryId=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private static int CopiesOf(IEnumerable<JsonElement> statuses, string status) =>
        statuses.Single(item => item.GetProperty("status").GetString() == status).GetProperty("copies").GetInt32();

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
