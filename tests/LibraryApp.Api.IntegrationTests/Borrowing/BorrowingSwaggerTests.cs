using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LibraryApp.Api.OpenApi;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// NFR-11 and D4 for the Borrowing endpoints: every operation in <c>swagger.json</c> has a summary and remarks, documents
/// 401 and 403, its success code, 400 when it takes a body, and only status codes the API really returns; every Borrowing
/// request and response schema has an example and a description for every property; the request examples work as they
/// are on the freshly seeded Development data.
/// </summary>
public sealed class BorrowingSwaggerTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly string[] Prefixes = ["/api/loans", "/api/reservations", "/api/penalties"];

    private static readonly HashSet<string> AllowedCodes = ["200", "201", "400", "401", "403", "404", "409", "422"];

    private static readonly string[] Schemas =
    [
        "IssueLoanRequest", "ReturnLoanRequest", "CreateReservationRequest", "WaivePenaltyRequest",
        "LoanDto", "ReturnLoanResponse", "DeclareLostResponse", "LoanHistoryItemDto", "ReservationDto", "PenaltyDto",
        // The pages of GET /api/loans and GET /api/penalties.
        "LoanHistoryItemDtoPagedResult", "PenaltyDtoPagedResult",
    ];

    private async Task<JsonElement> SwaggerAsync()
    {
        using var client = factory.CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
    }

    private static IEnumerable<(string Name, JsonElement Operation)> Operations(JsonElement swagger) =>
        swagger.GetProperty("paths").EnumerateObject()
            .Where(path => Prefixes.Any(prefix => path.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => method.Name is "get" or "post" or "put" or "delete")
                .Select(method => ($"{method.Name.ToUpperInvariant()} {path.Name}", method.Value)));

    [Fact]
    public async Task Every_borrowing_operation_is_documented_with_its_responses()
    {
        var operations = Operations(await SwaggerAsync()).ToList();
        Assert.Equal(11, operations.Count);

        foreach (var (name, operation) in operations)
        {
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()), $"{name}: no summary");
            Assert.True(operation.TryGetProperty("description", out var remarks) && remarks.GetString()!.Length > 40, $"{name}: no remarks");

            string[] codes = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToArray();
            Assert.Contains("401", codes);
            Assert.Contains("403", codes);
            Assert.True(codes.Contains("200") || codes.Contains("201"), $"{name}: no success response");
            Assert.All(codes, code => Assert.True(AllowedCodes.Contains(code), $"{name}: unexpected response {code}"));
            if (operation.TryGetProperty("requestBody", out _))
                Assert.Contains("400", codes);
            if (name.StartsWith("POST", StringComparison.Ordinal))
                Assert.Contains("404", codes);
        }
    }

    [Theory]
    [InlineData("/api/loans", "LoanHistoryItemDtoPagedResult", "loanId")]
    [InlineData("/api/penalties", "PenaltyDtoPagedResult", "penaltyId")]
    public async Task The_paged_lists_return_a_page_schema_whose_example_holds_an_item(string path, string schemaName, string itemKey)
    {
        JsonElement swagger = await SwaggerAsync();

        string reference = swagger.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        Assert.EndsWith($"/{schemaName}", reference);

        JsonElement example = swagger.GetProperty("components").GetProperty("schemas").GetProperty(schemaName).GetProperty("example");
        JsonElement item = Assert.Single(example.GetProperty("items").EnumerateArray());
        Assert.True(item.TryGetProperty(itemKey, out _), $"{schemaName}: the item of the example is not a {itemKey} record");
        Assert.Equal((1, 1, 20, 1), (example.GetProperty("totalCount").GetInt32(), example.GetProperty("page").GetInt32(),
            example.GetProperty("pageSize").GetInt32(), example.GetProperty("totalPages").GetInt32()));
    }

    [Theory]
    [InlineData("IssueLoanRequest", "/api/loans")]
    [InlineData("CreateReservationRequest", "/api/reservations")]
    public async Task The_request_example_works_on_the_seeded_data(string schema, string endpoint)
    {
        JsonElement example = (await SwaggerAsync()).GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("example");
        using var client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(StaffRoles.Librarian)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsync(endpoint, new StringContent(example.GetRawText(), Encoding.UTF8, "application/json"));

        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{schema}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains((schema == "IssueLoanRequest" ? BorrowingExamples.IssueStaffMember : BorrowingExamples.ReservationStaffMember).ToString(), body);
    }

    [Fact]
    public async Task Every_borrowing_schema_has_an_example_and_described_properties()
    {
        var schemas = (await SwaggerAsync()).GetProperty("components").GetProperty("schemas");

        foreach (string name in Schemas)
        {
            Assert.True(schemas.TryGetProperty(name, out var schema), $"Missing schema: {name}");
            Assert.True(schema.TryGetProperty("example", out var example) && example.ValueKind == JsonValueKind.Object, $"{name}: no example");
            foreach (var property in schema.GetProperty("properties").EnumerateObject())
            {
                // An enum property is a $ref to the enum schema (OpenAPI 3.0 shows nothing next to a $ref): the enum is described.
                JsonElement described = property.Value.TryGetProperty("$ref", out var reference)
                    ? schemas.GetProperty(reference.GetString()!.Split('/')[^1])
                    : property.Value;
                Assert.True(described.TryGetProperty("description", out var description) && description.GetString()!.Length > 0,
                    $"{name}.{property.Name}: no description");
                Assert.True(example.TryGetProperty(property.Name, out _), $"{name}.{property.Name}: not in the example");
            }
        }
    }
}
