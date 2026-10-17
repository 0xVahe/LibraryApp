using System.Text.Json;
using System.Net.Http.Json;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;

namespace LibraryApp.Api.IntegrationTests.Catalog;

public sealed class CatalogSwaggerTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task Catalog_request_schemas_have_examples_and_controller_tags()
    {
        using var client = factory.CreateClient();
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        var schemas = swagger.GetProperty("components").GetProperty("schemas");
        foreach (var schemaName in new[]
        {
            "CreateBookRequest",
            "UpdateBookRequest",
            "AddCopyRequest",
            "UpdateCopyRequest",
            "ChangeCopyStatusRequest",
            "AuthorRequest",
            "CategoryRequest",
            "PublisherRequest",
            "BookAuthorsRequest",
            "CreateBookSuggestionRequest",
            "RejectBookSuggestionRequest",
        })
        {
            Assert.True(schemas.TryGetProperty(schemaName, out var schema), $"Missing schema: {schemaName}");
            Assert.True(schema.TryGetProperty("example", out _), $"Missing example: {schemaName}");
        }

        var createBook = schemas.GetProperty("CreateBookRequest").GetProperty("example");
        Assert.Equal(CatalogSeedIds.Category(3).ToString(), createBook.GetProperty("categoryId").GetString());
        Assert.Equal(CatalogSeedData.Publishers[11].Id.ToString(), createBook.GetProperty("publisherId").GetString());
        Assert.Equal(
            new[] { CatalogSeedData.Authors[16].Id.ToString(), CatalogSeedData.Authors[23].Id.ToString() },
            createBook.GetProperty("authorIds").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Matches("^978\\d{10}$", createBook.GetProperty("isbn").GetString());

        var createBookProperties = schemas.GetProperty("CreateBookRequest").GetProperty("properties");
        Assert.False(string.IsNullOrWhiteSpace(createBookProperties.GetProperty("title").GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(createBookProperties.GetProperty("isbn").GetProperty("description").GetString()));

        // Registering CatalogExamples must not replace examples supplied by other schema filters.
        Assert.True(
            schemas.GetProperty("EnrolStaffMemberRequest").GetProperty("example").TryGetProperty("departmentId", out _),
            "StaffMemberExamples example was lost while registering CatalogExamples.");

        var paths = swagger.GetProperty("paths");
        var expectedCatalogTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "Authors",
            "BookCopies",
            "Books",
            "BookSuggestions",
            "Categories",
            "Publishers",
        };
        var seenCatalogTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths.EnumerateObject().Where(x =>
                     x.Name.StartsWith("/api/books", StringComparison.Ordinal)
                     || x.Name.StartsWith("/api/authors", StringComparison.Ordinal)
                     || x.Name.StartsWith("/api/categories", StringComparison.Ordinal)
                     || x.Name.StartsWith("/api/publishers", StringComparison.Ordinal)
                     || x.Name.StartsWith("/api/book-suggestions", StringComparison.Ordinal)))
        {
            foreach (var operation in path.Value.EnumerateObject().Where(x => x.Name is "get" or "post" or "put" or "delete"))
            {
                var tags = operation.Value.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ToArray();
                Assert.DoesNotContain("Catalog", tags);
                Assert.Contains(tags, tag => tag is not null && expectedCatalogTags.Contains(tag));
                seenCatalogTags.UnionWith(tags.OfType<string>());
            }
        }

        Assert.True(expectedCatalogTags.IsSubsetOf(seenCatalogTags));
    }
}
