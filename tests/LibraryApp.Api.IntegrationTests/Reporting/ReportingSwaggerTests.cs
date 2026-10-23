using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryApp.Api.IntegrationTests.Reporting;

/// <summary>
/// NFR-11 for every report (REP-01 to REP-07): every response schema has an example with all of its properties, and every
/// property is described.
/// </summary>
public sealed class ReportingSwaggerTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly string[] Schemas =
    [
        "InventoryStatusReportDto", "InventoryStatusItemDto", "InventoryConditionItemDto",
        "PurchaseCandidatesReportDto", "PurchaseCandidateItemDto",
        "OverdueLoansReportDto", "OverdueLoanItemDto", "DashboardReportDto",
        "TopBooksAndCategoriesReportDto", "TopBookReportItemDto", "TopCategoryReportItemDto",
        "ReadingActivityByDepartmentReportDto", "DepartmentReadingActivityDto",
        "PenaltyReportDto", "DepartmentPenaltiesDto",
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

    [Theory]
    [InlineData("/api/reports/overdue-loans")]
    [InlineData("/api/reports/dashboard")]
    [InlineData("/api/reports/top-books-and-categories")]
    [InlineData("/api/reports/reading-activity-by-department")]
    [InlineData("/api/reports/penalties")]
    [InlineData("/api/reports/inventory-status")]
    [InlineData("/api/reports/purchase-candidates")]
    public async Task Every_report_endpoint_has_a_summary_and_documents_the_format(string path)
    {
        using var client = factory.CreateClient();
        JsonElement get = (await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json"))
            .GetProperty("paths").GetProperty(path).GetProperty("get");

        Assert.True(get.TryGetProperty("summary", out var summary) && summary.GetString()!.Length > 0, $"{path}: no summary");
        JsonElement format = get.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "format");
        Assert.Contains("csv", format.GetProperty("description").GetString());
        Assert.True(get.GetProperty("responses").TryGetProperty("400", out _), $"{path}: no 400 response");
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
