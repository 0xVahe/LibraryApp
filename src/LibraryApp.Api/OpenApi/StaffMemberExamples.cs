using LibraryApp.Application.Staff.Dto;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>Synthetic examples for Staff's requests and responses (NFR-11)</summary>
public sealed class StaffMemberExamples : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(MyLibraryDto))
        {
            schema.Example = MyLibraryExample();
            return;
        }

        if (context.Type == typeof(DeactivateStaffMemberRequest))
        {
            schema.Example = new OpenApiObject { ["reason"] = new OpenApiString("Left the bank") };
            return;
        }

        if (context.Type == typeof(UpdateMyProfileRequest))
        {
            schema.Example = new OpenApiObject
            {
                ["firstName"] = new OpenApiString("Anna"),
                ["lastName"] = new OpenApiString("Petrosyan"),
            };
            return;
        }

        if (context.Type != typeof(EnrolStaffMemberRequest) && context.Type != typeof(UpdateStaffMemberRequest))
            return;

        var example = new OpenApiObject
        {
            ["firstName"] = new OpenApiString("Demo"),
            ["lastName"] = new OpenApiString("Employee"),
            ["corporateEmail"] = new OpenApiString("demo.employee@ameriabank.am"),
            ["departmentId"] = new OpenApiString(StaffSeedIds.Departments.InformationTechnology.ToString()),
            ["position"] = new OpenApiString("Engineer"),
            ["lineManagerEmail"] = new OpenApiNull(),
            ["staffCategoryId"] = new OpenApiString(StaffSeedIds.Categories.Employee.ToString()),
        };
        if (context.Type == typeof(EnrolStaffMemberRequest))
        {
            example["employeeId"] = new OpenApiString("DEMO-100");
            example["password"] = new OpenApiString("DemoPass1");
            example["role"] = new OpenApiString("StaffMember");
        }

        schema.Example = example;
    }

    private static OpenApiObject MyLibraryExample() => new()
    {
        ["profile"] = new OpenApiObject
        {
            ["id"] = new OpenApiString(StaffSeedIds.StaffMembers.AnnaPetrosyan.ToString()),
            ["firstName"] = new OpenApiString("Anna"),
            ["lastName"] = new OpenApiString("Petrosyan"),
            ["corporateEmail"] = new OpenApiString("anna.petrosyan@ameriabank.am"),
            ["departmentName"] = new OpenApiString("Retail Banking"),
            ["position"] = new OpenApiString("Analyst"),
            ["staffCategoryName"] = new OpenApiString("Employee"),
            ["status"] = new OpenApiString("Active"),
        },
        ["activeLoans"] = new OpenApiArray
        {
            new OpenApiObject
            {
                ["loanId"] = new OpenApiString("30000000-0000-0000-0001-000000000001"),
                ["bookId"] = new OpenApiString("10000000-0000-0000-0001-000000000001"),
                ["title"] = new OpenApiString("Risk Management Basics"),
                ["barcode"] = new OpenApiString("BK-0001-01"),
                ["issuedAtUtc"] = new OpenApiString("2026-10-01T09:00:00Z"),
                ["dueAtUtc"] = new OpenApiString("2026-10-22T09:00:00Z"),
                ["isOverdue"] = new OpenApiBoolean(false),
                ["renewalCount"] = new OpenApiInteger(0),
            },
        },
        ["reservations"] = new OpenApiArray
        {
            new OpenApiObject
            {
                ["reservationId"] = new OpenApiString("30000000-0000-0000-0002-000000000001"),
                ["bookId"] = new OpenApiString("10000000-0000-0000-0001-000000000002"),
                ["title"] = new OpenApiString("Clean Code in C#"),
                ["status"] = new OpenApiString("Waiting"),
                ["queuePosition"] = new OpenApiInteger(2),
                ["readyUntilUtc"] = new OpenApiNull(),
                ["createdAtUtc"] = new OpenApiString("2026-10-05T12:00:00Z"),
            },
        },
        ["activePenaltyPoints"] = new OpenApiInteger(1),
        ["activePenalties"] = new OpenApiArray
        {
            new OpenApiObject
            {
                ["penaltyId"] = new OpenApiString("30000000-0000-0000-0003-000000000001"),
                ["bookId"] = new OpenApiString("10000000-0000-0000-0001-000000000003"),
                ["title"] = new OpenApiString("Banking Law Essentials"),
                ["points"] = new OpenApiInteger(1),
                ["reason"] = new OpenApiString("Returned 3 days late"),
                ["createdAtUtc"] = new OpenApiString("2026-09-20T10:00:00Z"),
                ["expiresAtUtc"] = new OpenApiString("2026-12-19T10:00:00Z"),
            },
        },
    };
}
