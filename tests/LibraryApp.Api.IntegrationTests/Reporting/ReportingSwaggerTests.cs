using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryApp.Api.IntegrationTests.Reporting;

/// <summary>
/// NFR-11 for the inventory status (REP-05) and purchase candidates (REP-07) reports: every response schema has an example
/// with all of its properties, and every property is described.
/// </summary>
public sealed class ReportingSwaggerTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly string[] Schemas =
    [
        "InventoryStatusReportDto", "InventoryStatusItemDto", "InventoryConditionItemDto",
        "PurchaseCandidatesReportDto", "PurchaseCandidateItemDto",
    ];

    [Fact]
    public async Task Every_report_schema_has_an_example_and_described_properties()
    {
        using var client = factory.CreateClient();
        JsonElement schemas = (await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json"))
            .GetProperty("components").GetProperty("schemas");

        foreach (var name in Schemas)
        {
            Assert.True(schemas.TryGetProperty(name, out var schema), $"Missing schema: {name}");
            Assert.True(schema.TryGetProperty("example", out var example) && example.ValueKind == JsonValueKind.Object, $"{name}: no example");
            foreach (var property in schema.GetProperty("properties").EnumerateObject())
            {
                // An enum property is a $ref to the enum schema: the enum is described.
                var described = property.Value.TryGetProperty("$ref", out var reference)
                    ? schemas.GetProperty(reference.GetString()!.Split('/')[^1])
                    : property.Value;
                Assert.True(described.TryGetProperty("description", out var description) && description.GetString()!.Length > 0,
                    $"{name}.{property.Name}: no description");
                Assert.True(example.TryGetProperty(property.Name, out _), $"{name}.{property.Name}: not in the example");
            }
        }
    }

    [Fact]
    public async Task The_inventory_example_lists_every_status_and_its_totals_add_up()
    {
        using var client = factory.CreateClient();
        JsonElement example = (await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json"))
            .GetProperty("components").GetProperty("schemas").GetProperty("InventoryStatusReportDto").GetProperty("example");

        List<JsonElement> statuses = example.GetProperty("statuses").EnumerateArray().ToList();
        Assert.Equal(Enum.GetNames<LibraryApp.Domain.Catalog.BookCopyStatus>(), statuses.Select(s => s.GetProperty("status").GetString()));
        Assert.Equal(example.GetProperty("totalCopies").GetInt32(), statuses.Sum(s => s.GetProperty("copies").GetInt32()));
        Assert.All(statuses, s => Assert.Equal(
            s.GetProperty("copies").GetInt32(),
            s.GetProperty("conditions").EnumerateArray().Sum(c => c.GetProperty("copies").GetInt32())));
    }
}
