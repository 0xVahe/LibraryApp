using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>
/// Response examples of every report (REP-01 to REP-07, NFR-11), with the books, categories, departments and staff of the
/// Development seed.
/// </summary>
public sealed class ReportingExamples : ISchemaFilter
{
    private const string AsOfDate = "2026-10-22";
    private const string GeneratedAtUtc = "2026-10-22T06:30:00Z";
    private const string PeriodStart = "2026-10-01";
    private const string PeriodEnd = "2026-10-31";

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
            Type type when type == typeof(OverdueLoansReportDto) => OverdueLoans(),
            Type type when type == typeof(OverdueLoanItemDto) => OverdueLoan(),
            Type type when type == typeof(DashboardReportDto) => Dashboard(),
            Type type when type == typeof(TopBooksAndCategoriesReportDto) => new OpenApiObject
            {
                ["periodStart"] = new OpenApiString(PeriodStart),
                ["periodEnd"] = new OpenApiString(PeriodEnd),
                ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
                ["topBooks"] = new OpenApiArray { TopBook(0, 3), TopBook(1, 2) },
                ["topCategories"] = new OpenApiArray { TopCategory(0, 4), TopCategory(2, 1) },
            },
            Type type when type == typeof(TopBookReportItemDto) => TopBook(0, 3),
            Type type when type == typeof(TopCategoryReportItemDto) => TopCategory(0, 4),
            Type type when type == typeof(ReadingActivityByDepartmentReportDto) => new OpenApiObject
            {
                ["periodStart"] = new OpenApiString(PeriodStart),
                ["periodEnd"] = new OpenApiString(PeriodEnd),
                ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
                ["departments"] = new OpenApiArray { ReadingActivity(3, 4, 3, 2), ReadingActivity(5, 2, 0, null) },
            },
            Type type when type == typeof(DepartmentReadingActivityDto) => ReadingActivity(3, 4, 3, 2),
            Type type when type == typeof(PenaltyReportDto) => new OpenApiObject
            {
                ["periodStart"] = new OpenApiString(PeriodStart),
                ["periodEnd"] = new OpenApiString(PeriodEnd),
                ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
                ["departments"] = new OpenApiArray { DepartmentPenalties(0, 2, 7, 2, 5), DepartmentPenalties(5, 0, 0, 0, 0) },
            },
            Type type when type == typeof(DepartmentPenaltiesDto) => DepartmentPenalties(0, 2, 7, 2, 5),
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

    /// <summary>One overdue loan of Anna Petrosyan (seeded staff member), the only one of the page.</summary>
    private static OpenApiObject OverdueLoans() => new()
    {
        ["asOfDate"] = new OpenApiString(AsOfDate),
        ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
        ["departmentId"] = new OpenApiString(StaffSeedIds.Departments.RetailBanking.ToString()),
        ["minimumDaysOverdue"] = new OpenApiInteger(1),
        ["maximumDaysOverdue"] = new OpenApiInteger(5),
        ["items"] = new OpenApiArray { OverdueLoan() },
        ["totalCount"] = new OpenApiInteger(1),
        ["page"] = new OpenApiInteger(1),
        ["pageSize"] = new OpenApiInteger(20),
        ["totalPages"] = new OpenApiInteger(1),
    };

    /// <summary>Due on 16 Oct 2026 (Friday): 4 working days overdue on 22 Oct, so 4 points so far.</summary>
    private static OpenApiObject OverdueLoan()
    {
        SeedPerson anna = StaffSeedData.People.Single(person => person.StaffMemberId == StaffSeedIds.StaffMembers.AnnaPetrosyan);
        return new OpenApiObject
        {
            ["loanId"] = new OpenApiString("30000000-0000-0000-0001-000000000007"),
            ["employeeId"] = new OpenApiString(anna.EmployeeId),
            ["employeeName"] = new OpenApiString($"{anna.FirstName} {anna.LastName}"),
            ["department"] = new OpenApiString(DepartmentName(anna.DepartmentId)),
            ["lineManagerEmail"] = anna.LineManagerEmail is { } manager ? new OpenApiString(manager) : new OpenApiNull(),
            ["bookTitle"] = new OpenApiString(CatalogSeedData.Books[1].Title),
            ["dueDate"] = new OpenApiString("2026-10-16"),
            ["workingDaysOverdue"] = new OpenApiInteger(4),
            ["pointsSoFar"] = new OpenApiInteger(4),
        };
    }

    /// <summary>The books, copies and staff of the Development seed; 6 of its 40 loans are overdue.</summary>
    private static OpenApiObject Dashboard() => new()
    {
        ["asOfDate"] = new OpenApiString(AsOfDate),
        ["generatedAtUtc"] = new OpenApiString(GeneratedAtUtc),
        ["totalBooks"] = new OpenApiInteger(CatalogSeedData.Books.Count),
        ["totalCopies"] = new OpenApiInteger(CatalogSeedData.CopiesPerBook.Values.Sum()),
        ["activeReaders"] = new OpenApiInteger(StaffCount(StaffMemberStatus.Active)),
        ["activeLoans"] = new OpenApiInteger(19),
        ["overdueLoans"] = new OpenApiInteger(6),
        ["overduePercentage"] = new OpenApiDouble(15.0),
        ["restrictedEmployees"] = new OpenApiInteger(StaffCount(StaffMemberStatus.Restricted)),
        ["suspendedEmployees"] = new OpenApiInteger(StaffCount(StaffMemberStatus.Suspended)),
    };

    private static OpenApiObject TopBook(int index, int loans)
    {
        SeedBook book = CatalogSeedData.Books[index];
        return new OpenApiObject
        {
            ["bookId"] = new OpenApiString(CatalogSeedIds.Book(book.Number).ToString()),
            ["title"] = new OpenApiString(book.Title),
            ["loanCount"] = new OpenApiInteger(loans),
        };
    }

    private static OpenApiObject TopCategory(int index, int loans)
    {
        (Guid id, string name, _) = CatalogSeedData.Categories[index];
        return new OpenApiObject
        {
            ["categoryId"] = new OpenApiString(id.ToString()),
            ["name"] = new OpenApiString(name),
            ["loanCount"] = new OpenApiInteger(loans),
        };
    }

    /// <summary>A seeded department; without loans it has no top category.</summary>
    private static OpenApiObject ReadingActivity(int department, int employees, int loans, int? topCategory)
    {
        SeedDepartment seeded = StaffSeedData.DepartmentList[department];
        (Guid categoryId, string category, _) = CatalogSeedData.Categories[topCategory ?? 0];
        return new OpenApiObject
        {
            ["departmentId"] = new OpenApiString(seeded.Id.ToString()),
            ["department"] = new OpenApiString(seeded.Name),
            ["employees"] = new OpenApiInteger(employees),
            ["loanCount"] = new OpenApiInteger(loans),
            ["loansPer10Employees"] = new OpenApiDouble(employees == 0 ? 0 : Math.Round(loans * 10.0 / employees, 1)),
            ["topCategoryId"] = topCategory is null ? new OpenApiNull() : new OpenApiString(categoryId.ToString()),
            ["topCategory"] = topCategory is null ? new OpenApiNull() : new OpenApiString(category),
            ["topCategoryLoanCount"] = new OpenApiInteger(topCategory is null ? 0 : loans),
        };
    }

    /// <summary>Issued = waived + active + expired points of the records created in the period.</summary>
    private static OpenApiObject DepartmentPenalties(int department, int penalties, int issued, int waived, int active)
    {
        SeedDepartment seeded = StaffSeedData.DepartmentList[department];
        return new OpenApiObject
        {
            ["departmentId"] = new OpenApiString(seeded.Id.ToString()),
            ["department"] = new OpenApiString(seeded.Name),
            ["penalties"] = new OpenApiInteger(penalties),
            ["issuedPoints"] = new OpenApiInteger(issued),
            ["waivedPoints"] = new OpenApiInteger(waived),
            ["activePoints"] = new OpenApiInteger(active),
        };
    }

    private static string DepartmentName(Guid id) => StaffSeedData.DepartmentList.Single(department => department.Id == id).Name;

    private static int StaffCount(StaffMemberStatus status) => StaffSeedData.People.Count(person => person.Status == status);

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
