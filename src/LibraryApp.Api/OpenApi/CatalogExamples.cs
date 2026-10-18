using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>Valid request and cover response examples for Catalog endpoints (NFR-11).</summary>
public sealed class CatalogExamples(ICatalogCoverUrlBuilder coverUrls) : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(BookDto)
            || context.Type == typeof(BookDetailsDto)
            || context.Type == typeof(BookSearchResultDto))
        {
            if (schema.Properties.TryGetValue("coverImageUrl", out var coverImageUrl)
                && coverUrls.Build(CatalogSeedIds.CoverImagePath(1)) is { } coverUrl)
                coverImageUrl.Example = new OpenApiString(coverUrl);
        }

        OpenApiObject? example = context.Type switch
        {
            Type type when type == typeof(CreateBookRequest) => new OpenApiObject
            {
                ["title"] = new OpenApiString("Example Catalog Book"),
                ["isbn"] = new OpenApiString("9780132350884"),
                ["publicationYear"] = new OpenApiInteger(2020),
                ["language"] = new OpenApiString("en"),
                ["description"] = new OpenApiString("A valid example book for the Catalog API."),
                ["categoryId"] = new OpenApiString(CatalogSeedIds.Category(3).ToString()),
                ["publisherId"] = new OpenApiString(CatalogSeedData.Publishers[11].Id.ToString()),
                ["authorIds"] = Authors(17, 24),
                ["coverImagePath"] = new OpenApiString(CatalogSeedIds.CoverImagePath(15)),
            },
            Type type when type == typeof(UpdateBookRequest) => new OpenApiObject
            {
                ["title"] = new OpenApiString("Updated Catalog Book"),
                ["isbn"] = new OpenApiString("9780132350884"),
                ["publicationYear"] = new OpenApiInteger(2020),
                ["language"] = new OpenApiString("en"),
                ["description"] = new OpenApiString("Updated description."),
                ["categoryId"] = new OpenApiString(CatalogSeedIds.Category(3).ToString()),
                ["publisherId"] = new OpenApiString(CatalogSeedData.Publishers[11].Id.ToString()),
                ["coverImagePath"] = new OpenApiString(CatalogSeedIds.CoverImagePath(15)),
            },
            Type type when type == typeof(AddCopyRequest) => new OpenApiObject
            {
                ["barcode"] = new OpenApiString("SWAGGER-EXAMPLE-01"),
                ["condition"] = new OpenApiString("Good"),
                ["shelfLocation"] = new OpenApiString("S3-01-1"),
            },
            Type type when type == typeof(UpdateCopyRequest) => new OpenApiObject
            {
                ["condition"] = new OpenApiString("Good"),
                ["shelfLocation"] = new OpenApiString("S3-01-2"),
            },
            Type type when type == typeof(ChangeCopyStatusRequest) => new OpenApiObject
            {
                ["status"] = new OpenApiString("UnderRepair"),
            },
            Type type when type == typeof(AuthorRequest) => new OpenApiObject
            {
                ["fullName"] = new OpenApiString("Example Author"),
            },
            Type type when type == typeof(CategoryRequest) => new OpenApiObject
            {
                ["name"] = new OpenApiString("Example Category"),
                ["parentCategoryId"] = new OpenApiNull(),
            },
            Type type when type == typeof(PublisherRequest) => new OpenApiObject
            {
                ["name"] = new OpenApiString("Example Publisher"),
            },
            Type type when type == typeof(BookAuthorsRequest) => new OpenApiObject
            {
                ["authorIds"] = Authors(17, 24),
            },
            Type type when type == typeof(CreateBookSuggestionRequest) => new OpenApiObject
            {
                ["title"] = new OpenApiString("Example Suggested Book"),
                ["authorName"] = new OpenApiString("Example Author"),
                ["isbn"] = new OpenApiString("9780132350884"),
                ["reason"] = new OpenApiString("Useful for the software engineering collection."),
            },
            Type type when type == typeof(RejectBookSuggestionRequest) => new OpenApiObject
            {
                ["comment"] = new OpenApiString("This title is already available in the catalog."),
            },
            _ => null,
        };

        if (example is not null)
            schema.Example = example;
    }

    private static OpenApiArray Authors(int first, int second) =>
    [
        new OpenApiString(CatalogSeedData.Authors[first - 1].Id.ToString()),
        new OpenApiString(CatalogSeedData.Authors[second - 1].Id.ToString()),
    ];
}
