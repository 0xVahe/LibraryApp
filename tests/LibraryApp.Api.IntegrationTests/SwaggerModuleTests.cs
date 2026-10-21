using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Api.OpenApi;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>
/// NFR-11: Swagger groups the endpoints by module — one document per module next to the complete <c>v1</c>, offered in the
/// Swagger UI. Every endpoint of <c>v1</c> is in exactly one module document, so a controller outside the module folders
/// fails here.
/// </summary>
public sealed class SwaggerModuleTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete"];

    private async Task<HashSet<string>> OperationsAsync(string document)
    {
        using var client = factory.CreateClient();
        var swagger = await client.GetFromJsonAsync<JsonElement>($"/swagger/{document}/swagger.json");
        return swagger.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => Methods.Contains(method.Name))
                .Select(method => $"{method.Name.ToUpperInvariant()} {path.Name}"))
            .ToHashSet();
    }

    [Fact]
    public async Task Every_endpoint_of_the_complete_document_is_in_exactly_one_module_document()
    {
        HashSet<string> all = await OperationsAsync(ModuleDocuments.All);
        var seen = new Dictionary<string, string>();

        foreach (var (name, _, _) in ModuleDocuments.Modules)
        {
            HashSet<string> module = await OperationsAsync(name);
            Assert.NotEmpty(module);
            foreach (string operation in module)
            {
                Assert.Contains(operation, all);
                Assert.True(seen.TryAdd(operation, name), $"{operation} is in both {seen.GetValueOrDefault(operation)} and {name}");
            }
        }

        Assert.Empty(all.Except(seen.Keys));
    }

    [Theory]
    [InlineData("catalog", "GET /api/books")]
    [InlineData("staff", "POST /api/auth/login")]
    [InlineData("borrowing", "POST /api/loans")]
    [InlineData("notifications", "GET /api/notifications/me")]
    [InlineData("reporting", "GET /api/reports/overdue-loans")]
    public async Task Each_module_document_holds_its_own_endpoints(string module, string operation)
    {
        Assert.Contains(operation, await OperationsAsync(module));
        foreach (var (other, _, _) in ModuleDocuments.Modules.Where(m => m.Name != module))
            Assert.DoesNotContain(operation, await OperationsAsync(other));
    }

    [Fact]
    public async Task The_swagger_ui_offers_the_complete_document_first_and_then_every_module()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The UI configuration (the list of documents) is served with the page script.
        string page = await response.Content.ReadAsStringAsync() + await client.GetStringAsync("/swagger/index.js");
        int complete = page.IndexOf("/swagger/v1/swagger.json", StringComparison.Ordinal);
        Assert.True(complete >= 0, "the complete document is not offered");
        foreach (var (name, _, title) in ModuleDocuments.Modules)
        {
            int position = page.IndexOf($"/swagger/{name}/swagger.json", StringComparison.Ordinal);
            Assert.True(position > complete, $"{title} is not offered after the complete document");
        }
    }
}
