using LibraryApp.Application.Staff.Dto;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>Synthetic examples for Staff's management requests (NFR-11).</summary>
public sealed class StaffMemberExamples : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
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
}
