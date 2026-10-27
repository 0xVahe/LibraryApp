using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-10: real controllers, services, JWT, validators and ProblemDetails; test-only persistence.</summary>
public sealed class BookSuggestionHostFactory : ApiHostFactory
{
    // JWT lifetime validation uses real time; keep a stable instant within the token's current lifetime.
    public static readonly DateTime Now = DateTime.UtcNow;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting("UseHttpsRedirection", "false");
        builder.UseSetting("Messaging:DispatcherEnabled", "false");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.ConfigureServices(services =>
        {
            // Real accounts for JWT validation; the Catalog repositories below still use test persistence.
            services.RemoveAll<IModuleSeeder>();
            services.AddScoped<IModuleSeeder, StaffSeeder>();
            services.AddSingleton<Store>();
            services.Replace(ServiceDescriptor.Singleton<IBookSuggestionRepository>(sp => sp.GetRequiredService<Store>()));
            services.Replace(ServiceDescriptor.Singleton<IApplicationLock>(sp => sp.GetRequiredService<Store>()));
            services.Replace(ServiceDescriptor.Singleton<IStaffMemberService, StaffNames>());
            services.Replace(ServiceDescriptor.Singleton<IClock, FixedClock>());
        });
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateOnly LocalToday => DateOnly.FromDateTime(Now);
        public DateTime ToLocal(DateTime utc) => utc;
        public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc);
        public DateTime StartOfLocalDayUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        public DateTime EndOfLocalDayUtc(DateOnly date) => StartOfLocalDayUtc(date.AddDays(1)).AddTicks(-1);
    }

    private sealed class Store : IBookSuggestionRepository, IApplicationLock
    {
        private readonly List<BookSuggestion> _items = [];
        public void Add(BookSuggestion suggestion) => _items.Add(suggestion);
        public Task<BookSuggestion?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(_items.SingleOrDefault(x => x.Id == id));
        public Task AcquireAsync(string resource, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PagedResult<BookSuggestion>> ListAsync(Guid? staffMemberId, BookSuggestionStatus? status,
            PageRequest paging, CancellationToken ct)
        {
            var matches = _items.Where(x => staffMemberId is null || x.StaffMemberId == staffMemberId)
                .Where(x => status is null || x.Status == status).OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray();
            var items = matches.Skip(paging.Skip).Take(paging.PageSize).ToArray();
            return Task.FromResult(new PagedResult<BookSuggestion>(items, matches.Length, paging.Page, paging.PageSize));
        }
    }

    /// <summary>Every staff member is "Test Member"; the other Staff methods are not used by CAT-10.</summary>
    private sealed class StaffNames : IStaffMemberService
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> staffMemberIds,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(staffMemberIds.Distinct().ToDictionary(id => id, _ => "Test Member"));

        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

public class BookSuggestionEndpointTests(BookSuggestionHostFactory factory) : IClassFixture<BookSuggestionHostFactory>
{
    private const string Route = "/api/book-suggestions";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private HttpClient Client(string? role, Guid? staffId = null)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role) with { StaffMemberId = staffId }).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static async Task<BookSuggestionDto> Create(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(Route, new CreateBookSuggestionRequest("Title", "Author", "9780132350884", "Reason"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookSuggestionDto>(Json))!;
    }

    private static Task<PagedResult<BookSuggestionDto>?> Page(HttpClient client, string path) =>
        client.GetFromJsonAsync<PagedResult<BookSuggestionDto>>(path, Json);

    [Theory]
    [InlineData(StaffRoles.StaffMember)]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Library_users_create_and_list_own_suggestions_without_overriding_server_fields(string role)
    {
        var owner = Guid.NewGuid();
        using var client = Client(role, owner);
        using var response = await client.PostAsJsonAsync(Route, new
        {
            title = "  Title  ", authorName = " Author ", reason = " Reason ",
            id = Guid.Empty, staffMemberId = Guid.NewGuid(), staffMemberName = "Injected", status = "Approved",
            createdAtUtc = "2000-01-01T00:00:00Z", reviewedAtUtc = "2001-01-01T00:00:00Z",
            reviewedByUserId = Guid.NewGuid(), reviewComment = "Injected",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BookSuggestionDto>(Json))!;
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(owner, result.StaffMemberId);
        Assert.Equal("Test Member", result.StaffMemberName);
        Assert.Equal(BookSuggestionStatus.Pending, result.Status);
        Assert.Equal(BookSuggestionHostFactory.Now, result.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAtUtc.Kind);
        Assert.Null(result.ReviewedAtUtc);
        Assert.Null(result.ReviewedByUserId);
        Assert.Null(result.ReviewComment);
        Assert.Null(result.Isbn);
        Assert.Equal("Title", result.Title);
        Assert.Equal(result, Assert.Single((await Page(client, Route + "/me"))!.Items));
        Assert.Equal(new[] { "AuthorName", "Isbn", "Reason", "Title" },
            typeof(CreateBookSuggestionRequest).GetProperties().Select(x => x.Name).Order());
    }

    [Fact]
    public async Task Two_staff_members_see_only_their_own_suggestions_even_with_spoofed_query()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        using var first = Client(StaffRoles.StaffMember, firstId);
        using var second = Client(StaffRoles.StaffMember, secondId);
        var one = await Create(first);
        var two = await Create(second);
        Assert.Equal(one.Id, Assert.Single((await Page(first, $"{Route}/me?staffMemberId={secondId}"))!.Items).Id);
        Assert.Equal(two.Id, Assert.Single((await Page(second, $"{Route}/me?staffMemberId={firstId}"))!.Items).Id);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/me")]
    [InlineData("POST", "")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/approve")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/reject")]
    public async Task Anonymous_requests_receive_401(string method, string suffix)
    {
        using var client = Client(null);
        using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix) { Content = JsonContent.Create(new { comment = "Reason" }) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember, "")]
    [InlineData(StaffRoles.StaffMember, "/approve")]
    [InlineData(StaffRoles.StaffMember, "/reject")]
    [InlineData(StaffRoles.Librarian, "")]
    [InlineData(StaffRoles.Librarian, "/approve")]
    [InlineData(StaffRoles.Librarian, "/reject")]
    public async Task Non_administrators_receive_403_for_all_list_and_reviews(string role, string operation)
    {
        using var client = Client(role, Guid.NewGuid());
        using var response = operation == "" ? await client.GetAsync(Route)
            : await client.PostAsJsonAsync($"{Route}/{Guid.NewGuid()}{operation}", new { comment = "Reason" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember, false)]
    [InlineData(StaffRoles.StaffMember, true)]
    [InlineData(StaffRoles.Librarian, false)]
    [InlineData(StaffRoles.Librarian, true)]
    [InlineData(StaffRoles.LibraryAdministrator, false)]
    [InlineData(StaffRoles.LibraryAdministrator, true)]
    public async Task Library_users_without_valid_staff_identity_cannot_create_or_read_own_data(string role, bool empty)
    {
        using var client = Client(role, empty ? Guid.Empty : null);
        using var create = await client.PostAsJsonAsync(Route, new CreateBookSuggestionRequest("Title", "Author", null, "Reason"));
        using var list = await client.GetAsync(Route + "/me");
        await Problem(create, HttpStatusCode.Forbidden, "catalog.suggestion_staff_identity_required");
        await Problem(list, HttpStatusCode.Forbidden, "catalog.suggestion_staff_identity_required");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_review_records_administrator_and_utc_metadata(bool reject)
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        var suggestion = await Create(staff);
        var reviewer = factory.Account(StaffRoles.LibraryAdministrator).UserId;
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var response = await admin.PostAsJsonAsync($"{Route}/{suggestion.Id}/{(reject ? "reject" : "approve")}",
            new { comment = "  Review comment  ", reviewedByUserId = Guid.NewGuid(), reviewedAtUtc = "2000-01-01T00:00:00Z" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BookSuggestionDto>(Json))!;
        Assert.Equal(reject ? BookSuggestionStatus.Rejected : BookSuggestionStatus.Approved, result.Status);
        Assert.Equal(reviewer, result.ReviewedByUserId);
        Assert.Equal(BookSuggestionHostFactory.Now, result.ReviewedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.ReviewedAtUtc!.Value.Kind);
        Assert.Equal(reject ? "Review comment" : null, result.ReviewComment);
        Assert.Equal(suggestion.StaffMemberId, result.StaffMemberId);
        Assert.Equal(suggestion.StaffMemberName, result.StaffMemberName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"comment\":null}")]
    [InlineData("{\"comment\":\"\"}")]
    [InlineData("{\"comment\":\"  \\t\\r\\n\"}")]
    public async Task Missing_empty_and_whitespace_rejection_comments_return_400(string body)
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        var suggestion = await Create(staff);
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var response = await admin.PostAsync($"{Route}/{suggestion.Id}/reject", new StringContent(body, Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(BookSuggestionStatus.Pending, Assert.Single((await Page(staff, Route + "/me"))!.Items).Status);
    }

    [Theory]
    [InlineData("approve", "approve")]
    [InlineData("approve", "reject")]
    [InlineData("reject", "approve")]
    [InlineData("reject", "reject")]
    public async Task Every_completed_review_transition_returns_409_and_preserves_metadata(string first, string second)
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        var suggestion = await Create(staff);
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var initial = await admin.PostAsJsonAsync($"{Route}/{suggestion.Id}/{first}", new { comment = "First review" });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var reviewed = await initial.Content.ReadFromJsonAsync<BookSuggestionDto>(Json);
        using var otherAdmin = Client(StaffRoles.LibraryAdministrator);
        using var response = await otherAdmin.PostAsJsonAsync($"{Route}/{suggestion.Id}/{second}", new { comment = "Second review" });
        await Problem(response, HttpStatusCode.Conflict, "catalog.suggestion_already_reviewed");
        Assert.Equal(reviewed, Assert.Single((await Page(staff, Route + "/me"))!.Items));
    }

    public static IEnumerable<object[]> InvalidReviewComments()
    {
        foreach (var state in new[] { "approve", "reject" })
        foreach (var comment in new[] { null, "", " \t\r\n", new string('c', 1001) })
            yield return [state, comment is null ? "{}" : JsonSerializer.Serialize(new { comment })];
    }

    [Theory]
    [MemberData(nameof(InvalidReviewComments))]
    public async Task Completed_suggestions_return_400_when_rejection_comment_is_invalid(string first, string body)
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        var suggestion = await Create(staff);
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var initial = await admin.PostAsJsonAsync($"{Route}/{suggestion.Id}/{first}", new { comment = "First review" });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var reviewed = await initial.Content.ReadFromJsonAsync<BookSuggestionDto>(Json);
        using var response = await admin.PostAsync($"{Route}/{suggestion.Id}/reject", new StringContent(body, Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(reviewed, Assert.Single((await Page(staff, Route + "/me"))!.Items));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Unknown_ids_return_404(string action)
    {
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var response = await admin.PostAsJsonAsync($"{Route}/{Guid.NewGuid()}/{action}", new { comment = "Comment" });
        await Problem(response, HttpStatusCode.NotFound, "catalog.suggestion_not_found");
    }

    [Fact]
    public async Task Administrator_lists_filter_status_and_page_with_total_count()
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        using var admin = Client(StaffRoles.LibraryAdministrator);
        for (var i = 0; i < 3; i++)
        {
            var suggestion = await Create(staff);
            using var response = await admin.PostAsJsonAsync($"{Route}/{suggestion.Id}/approve", new { });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var all = (await Page(admin, Route + "?status=Approved&pageSize=100"))!;
        Assert.True(all.TotalCount >= 3);
        Assert.All(all.Items, x => Assert.Equal(BookSuggestionStatus.Approved, x.Status));
        var page = (await Page(admin, Route + "?status=2&page=2&pageSize=1"))!;
        Assert.Equal(all.TotalCount, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(all.Items[1], Assert.Single(page.Items));
        var unfiltered = (await Page(admin, Route))!;
        Assert.True(unfiltered.TotalCount >= all.TotalCount);
    }

    public static IEnumerable<object[]> InvalidCreation()
    {
        yield return ["{}"];
        foreach (var field in new[] { "title", "authorName", "reason" })
        foreach (var value in new[] { "", " \t", new string('x', field == "title" ? 301 : field == "authorName" ? 201 : 1001) })
        {
            var input = new Dictionary<string, string?> { ["title"] = "Title", ["authorName"] = "Author", ["reason"] = "Reason" };
            input[field] = value;
            yield return [JsonSerializer.Serialize(input)];
        }
        foreach (var isbn in new[] { "", " ", "0132350883", "9780132350885", "978013235088X", "1234567890128" })
            yield return [JsonSerializer.Serialize(new CreateBookSuggestionRequest("Title", "Author", isbn, "Reason"))];
    }

    [Theory]
    [InlineData("0132350882")]
    [InlineData("978-0-13-235088-4")]
    [InlineData("978 0 13 235088 4")]
    public async Task ISBN_uses_the_existing_catalog_validation_and_normalization(string isbn)
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        using var response = await staff.PostAsJsonAsync(Route,
            new CreateBookSuggestionRequest("Title", "Author", isbn, "Reason"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BookSuggestionDto>(Json))!;
        Assert.Equal("9780132350884", result.Isbn);
        Assert.Equal(result, Assert.Single((await Page(staff, Route + "/me"))!.Items));
    }

    [Theory]
    [MemberData(nameof(InvalidCreation))]
    public async Task Invalid_creation_returns_400_without_persisting(string body)
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        using var response = await staff.PostAsync(Route, new StringContent(body, Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Empty((await Page(staff, Route + "/me"))!.Items);
    }

    [Fact]
    public async Task Maximum_lengths_are_accepted_and_overlong_rejection_is_400()
    {
        using var staff = Client(StaffRoles.StaffMember, Guid.NewGuid());
        using var created = await staff.PostAsJsonAsync(Route, new CreateBookSuggestionRequest(new('t', 300), new('a', 200), null, new('r', 1000)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var suggestion = (await created.Content.ReadFromJsonAsync<BookSuggestionDto>(Json))!;
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var invalid = await admin.PostAsJsonAsync($"{Route}/{suggestion.Id}/reject", new { comment = new string('c', 1001) });
        await Problem(invalid, HttpStatusCode.BadRequest, "validation_failed");
        using var valid = await admin.PostAsJsonAsync($"{Route}/{suggestion.Id}/reject", new { comment = new string('c', 1000) });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Theory]
    [InlineData("?status=0")]
    [InlineData("?status=4")]
    [InlineData("?status=Unknown")]
    [InlineData("?page=abc")]
    [InlineData("?pageSize=2147483648")]
    public async Task Invalid_query_values_return_400(string query)
    {
        using var admin = Client(StaffRoles.LibraryAdministrator);
        using var response = await admin.GetAsync(Route + query);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Paging_follows_catalog_normalization()
    {
        using var admin = Client(StaffRoles.LibraryAdministrator);
        var normalized = (await Page(admin, Route + "?page=-1&pageSize=0"))!;
        Assert.Equal((1, 20), (normalized.Page, normalized.PageSize));
        Assert.Equal(100, (await Page(admin, Route + "?pageSize=101"))!.PageSize);
    }

    [Fact]
    public async Task Swagger_documents_models_roles_filtering_paging_and_responses()
    {
        using var client = Client(null);
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = swagger.GetProperty("paths");
        Check(paths.GetProperty(Route).GetProperty("post"), "Any library user", ["201", "400", "401", "403", "409"]);
        var list = paths.GetProperty(Route).GetProperty("get");
        Check(list, "Administrator", ["200", "400", "401", "403"]);
        Assert.Equal(new[] { "page", "pagesize", "status" }, list.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!.ToLowerInvariant()).Order());
        var mine = paths.GetProperty(Route + "/me").GetProperty("get");
        Check(mine, "Any library user", ["200", "400", "401", "403"]);
        Assert.DoesNotContain(mine.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "staffMemberId");
        Check(paths.GetProperty(Route + "/{id}/approve").GetProperty("post"), "Administrator", ["200", "401", "403", "404", "409"]);
        Check(paths.GetProperty(Route + "/{id}/reject").GetProperty("post"), "Administrator", ["200", "400", "401", "403", "404", "409"]);
        var schemas = swagger.GetProperty("components").GetProperty("schemas");
        Assert.Equal(new[] { "authorName", "isbn", "reason", "title" }, schemas.GetProperty("CreateBookSuggestionRequest")
            .GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.True(schemas.GetProperty("BookSuggestionDto").GetProperty("properties").TryGetProperty("staffMemberName", out _));
        Assert.True(schemas.TryGetProperty("RejectBookSuggestionRequest", out _));

        static void Check(JsonElement operation, string role, string[] codes)
        {
            Assert.Contains(role, operation.GetProperty("summary").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
            foreach (var code in codes) Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _));
        }
    }

    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
