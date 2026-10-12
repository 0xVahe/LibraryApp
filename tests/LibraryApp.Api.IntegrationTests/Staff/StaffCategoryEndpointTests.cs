using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class StaffCategoryEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/staff-categories";

    private HttpClient Client(string? role = StaffRoles.LibraryAdministrator)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (role is not null)
        {
            var token = factory.Services
                .GetRequiredService<IAccessTokenIssuer>()
                .Issue(new LoginAccount(
                    Guid.NewGuid(),
                    "category-tests@ameriabank.am",
                    Guid.NewGuid(),
                    StaffMemberStatus.Active,
                    [role]))
                .Token;

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string UniqueName() => $"Category-{Guid.NewGuid():N}";

    private static async Task<JsonElement> AssertError(
        HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            body.GetProperty("traceId").GetString()));

        return body;
    }

    private static async Task AssertValidationError(
        HttpResponseMessage response, string field)
    {
        var body = await AssertError(
            response, HttpStatusCode.BadRequest, "validation_failed");

        var messages = body.GetProperty("errors").GetProperty(field);

        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.NotEmpty(messages.EnumerateArray());
    }

    [Fact]
    public async Task Admin_can_create_read_update_and_read_back()
    {
        using var client = Client();
        var request = new UpsertStaffCategoryRequest(
            UniqueName(), 4, 25, 0);

        using var create = await client.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content
            .ReadFromJsonAsync<StaffCategoryDto>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        var expected = new StaffCategoryDto(
            created.Id, request.Name, 4, 25, 0);
        Assert.Equal(expected, created);

        var location = create.Headers.Location;
        Assert.NotNull(location);
        Assert.EndsWith($"{Route}/{created.Id}", location.ToString());

        using var get = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(expected,
            await get.Content.ReadFromJsonAsync<StaffCategoryDto>());

        var updateRequest = new UpsertStaffCategoryRequest(
            UniqueName(), 6, 28, 2);
        using var update = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = new StaffCategoryDto(
            created.Id, updateRequest.Name, 6, 28, 2);
        Assert.Equal(updated,
            await update.Content.ReadFromJsonAsync<StaffCategoryDto>());

        using var readBack = await client.GetAsync($"{Route}/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        Assert.Equal(updated,
            await readBack.Content.ReadFromJsonAsync<StaffCategoryDto>());
    }

    [Theory]
    [InlineData(false, 0, 14, 1, "MaxLoans")]
    [InlineData(true, 0, 14, 1, "MaxLoans")]
    [InlineData(false, 2, 0, 1, "LoanDays")]
    [InlineData(true, 2, 0, 1, "LoanDays")]
    [InlineData(false, 2, 14, -1, "MaxRenewals")]
    [InlineData(true, 2, 14, -1, "MaxRenewals")]
    public async Task Invalid_limits_return_400_with_errors_per_field(
        bool update, int maxLoans, int loanDays, int maxRenewals,
        string field)
    {
        using var client = Client();
        var request = new UpsertStaffCategoryRequest(
            UniqueName(), maxLoans, loanDays, maxRenewals);

        using var response = update
            ? await client.PutAsJsonAsync($"{Route}/{Guid.NewGuid()}", request)
            : await client.PostAsJsonAsync(Route, request);

        await AssertValidationError(response, field);
    }

    [Fact]
    public async Task Empty_name_returns_400()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            Route, new UpsertStaffCategoryRequest("", 2, 14, 1));

        await AssertValidationError(response, "Name");
    }

    [Fact]
    public async Task Missing_category_returns_404_for_get_and_update()
    {
        using var client = Client();
        var path = $"{Route}/{Guid.NewGuid()}";

        using var get = await client.GetAsync(path);
        await AssertError(get, HttpStatusCode.NotFound,
            "staff.category_resource_not_found");

        using var update = await client.PutAsJsonAsync(
            path, new UpsertStaffCategoryRequest(UniqueName(), 2, 14, 1));
        await AssertError(update, HttpStatusCode.NotFound,
            "staff.category_resource_not_found");
    }

    [Fact]
    public async Task Duplicate_name_returns_409()
    {
        using var client = Client();
        var request = new UpsertStaffCategoryRequest(UniqueName(), 2, 14, 1);

        using var first = await client.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var duplicate = await client.PostAsJsonAsync(Route, request);
        await AssertError(duplicate, HttpStatusCode.Conflict,
            "staff.category_name_taken");
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task All_endpoints_require_administrator(
        string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        var path = $"{Route}/{Guid.NewGuid()}";
        var request = new UpsertStaffCategoryRequest(UniqueName(), 2, 14, 1);

        using var list = await client.GetAsync(Route);
        using var get = await client.GetAsync(path);
        using var post = await client.PostAsJsonAsync(Route, request);
        using var put = await client.PutAsJsonAsync(path, request);

        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected, get.StatusCode);
        Assert.Equal(expected, post.StatusCode);
        Assert.Equal(expected, put.StatusCode);
    }

    [Fact]
    public async Task Updating_limits_with_own_trimmed_name_succeeds()
    {
        using var client = Client();

        using var create = await client.PostAsJsonAsync(
            Route, new UpsertStaffCategoryRequest(UniqueName(), 2, 14, 1));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<StaffCategoryDto>();
        Assert.NotNull(created);

        using var update = await client.PutAsJsonAsync(
            $"{Route}/{created.Id}",
            new UpsertStaffCategoryRequest($"  {created.Name}  ", 6, 28, 2));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var expected = new StaffCategoryDto(
            created.Id, created.Name, 6, 28, 2);

        Assert.Equal(expected,
            await update.Content.ReadFromJsonAsync<StaffCategoryDto>());

        using var readBack = await client.GetAsync($"{Route}/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        Assert.Equal(expected,
            await readBack.Content.ReadFromJsonAsync<StaffCategoryDto>());
    }

    [Fact]
    public async Task Parallel_duplicate_creates_return_201_and_category_conflict()
    {
        using var client = Client();
        var request = new UpsertStaffCategoryRequest(UniqueName(), 2, 14, 1);

        var firstPost = client.PostAsJsonAsync(Route, request);
        var secondPost = client.PostAsJsonAsync(Route, request);

        var responses = await Task.WhenAll(firstPost, secondPost);

        using var first = responses[0];
        using var second = responses[1];

        var createdResponse = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);

        var conflictResponse = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);

        await AssertError(conflictResponse, HttpStatusCode.Conflict, "staff.category_name_taken");

        var created = await createdResponse.Content.ReadFromJsonAsync<StaffCategoryDto>();
        Assert.NotNull(created);

        using var listResponse = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var categories = await listResponse.Content.ReadFromJsonAsync<List<StaffCategoryDto>>();
        Assert.NotNull(categories);

        var stored = Assert.Single(categories, x => x.Name == request.Name);

        Assert.Equal(created, stored);
    }

    [Fact]
    public async Task List_returns_categories_in_name_order()
    {
        using var client = Client();
        var prefix = UniqueName();

        async Task<Guid> Create(string suffix, int maxLoans)
        {
            using var response = await client.PostAsJsonAsync(
                Route,
                new UpsertStaffCategoryRequest(
                    $"{prefix}-{suffix}", maxLoans, 14, 1));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var category = await response.Content
                .ReadFromJsonAsync<StaffCategoryDto>();
            Assert.NotNull(category);
            return category.Id;
        }

        var zulu = await Create("Zulu", 1);
        var middle = await Create("Middle", 2);
        var alpha = await Create("Alpha", 3);

        using var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var categories = await response.Content
            .ReadFromJsonAsync<List<StaffCategoryDto>>();
        Assert.NotNull(categories);

        var actual = categories
            .Where(x => x.Id == alpha || x.Id == middle || x.Id == zulu)
            .Select(x => x.Id)
            .ToArray();

        Assert.Equal(new[] { alpha, middle, zulu }, actual);
    }

    [Fact]
    public async Task Duplicate_update_returns_409_and_preserves_original_values()
    {
        using var client = Client();

        async Task<StaffCategoryDto> Create(string name)
        {
            using var response = await client.PostAsJsonAsync(
                Route, new UpsertStaffCategoryRequest(name, 2, 14, 1));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var category = await response.Content
                .ReadFromJsonAsync<StaffCategoryDto>();
            Assert.NotNull(category);
            return category;
        }

        var first = await Create(UniqueName());
        var second = await Create(UniqueName());

        using var update = await client.PutAsJsonAsync(
            $"{Route}/{second.Id}",
            new UpsertStaffCategoryRequest(first.Name, 6, 28, 2));

        await AssertError(update, HttpStatusCode.Conflict, "staff.category_name_taken");

        using var readBack = await client.GetAsync($"{Route}/{second.Id}");
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        Assert.Equal(second,
            await readBack.Content.ReadFromJsonAsync<StaffCategoryDto>());
    }
}
