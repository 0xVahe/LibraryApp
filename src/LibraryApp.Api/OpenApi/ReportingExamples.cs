using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>
/// Response examples of the inventory status (REP-05) and purchase candidates (REP-07) reports (NFR-11), with books and
/// categories of the Development seed.
/// </summary>
public sealed class ReportingExamples : ISchemaFilter
{
    private const string AsOfDate = "2026-10-22";
    private const string GeneratedAtUtc = "2026-10-22T06:30:00Z";

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        OpenApiObject? example = context.Type switch
        {
            Type type when type == typeof(InventoryStatusReportDto) => InventoryStatus(),
            Type type when type == typeof(InventoryStatusItemDto) => Status(BookCopyStatus.Available, (BookCopyCondition.Good, 5), (BookCopyCondition.Fair, 1)),
            Type type when type == typeof(InventoryConditionItemDto) => Condition(BookCopyCondition.Good, 5),
            Type type when type == typeof(PurchaseCandidatesReportDto) => new OpenApiObject
            {
                ["asOfDate"] = new OpenApiString(AsOfDate),
                ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
                ["limit"] = new OpenApiInteger(10),
                ["books"] = new OpenApiArray { PurchaseCandidate() },
            },
            Type type when type == typeof(PurchaseCandidateItemDto) => PurchaseCandidate(),
            _ => null,
        };

        if (example is not null)
            schema.Example = example;
    }

    /// <summary>Banking &amp; Finance with its subcategories: every status is listed, also with 0 copies.</summary>
    private static OpenApiObject InventoryStatus()
    {
        (Guid id, string name, _) = CatalogSeedData.Categories[0];
        var statuses = new OpenApiArray();
        statuses.AddRange(Enum.GetValues<BookCopyStatus>().Select(status => status switch
        {
            BookCopyStatus.Available => Status(status, (BookCopyCondition.Good, 5), (BookCopyCondition.Fair, 1)),
            BookCopyStatus.OnLoan => Status(status, (BookCopyCondition.Good, 2)),
            BookCopyStatus.Lost => Status(status, (BookCopyCondition.Good, 1)),
            _ => Status(status),
        }));

        return new OpenApiObject
        {
            ["asOfDate"] = new OpenApiString(AsOfDate),
            ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
            ["categoryId"] = new OpenApiString(id.ToString()),
            ["category"] = new OpenApiString(name),
            ["totalCopies"] = new OpenApiInteger(9),
            ["copiesInCollection"] = new OpenApiInteger(8),
            ["statuses"] = statuses,
        };
    }

    /// <summary>One status with every condition, the ones not given with 0 copies.</summary>
    private static OpenApiObject Status(BookCopyStatus status, params (BookCopyCondition Condition, int Copies)[] copies)
    {
        var conditions = new OpenApiArray();
        conditions.AddRange(Enum.GetValues<BookCopyCondition>().Select(condition =>
            Condition(condition, copies.Where(c => c.Condition == condition).Sum(c => c.Copies))));

        return new OpenApiObject
        {
            ["status"] = new OpenApiString(status.ToString()),
            ["copies"] = new OpenApiInteger(copies.Sum(c => c.Copies)),
            ["conditions"] = conditions,
        };
    }

    private static OpenApiObject Condition(BookCopyCondition condition, int copies) => new()
    {
        ["condition"] = new OpenApiString(condition.ToString()),
        ["copies"] = new OpenApiInteger(copies),
    };

    /// <summary>The first seeded book, with the two Waiting reservations of the Development seed.</summary>
    private static OpenApiObject PurchaseCandidate()
    {
        SeedBook book = CatalogSeedData.Books[0];
        return new OpenApiObject
        {
            ["bookId"] = new OpenApiString(CatalogSeedIds.Book(book.Number).ToString()),
            ["title"] = new OpenApiString(book.Title),
            ["isbn"] = new OpenApiString(book.Isbn),
            ["category"] = new OpenApiString(
                CatalogSeedData.Categories.Single(category => category.Id == CatalogSeedIds.Category(book.CategoryNumber)).Name),
            ["waitingReservations"] = new OpenApiInteger(2),
            ["copiesInCollection"] = new OpenApiInteger(CatalogSeedData.CopiesPerBook[book.Number]),
        };
    }
}
