using LibraryApp.Application.Staff.Dto;
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
            ["departmentId"] = new OpenApiString("20000000-0000-0000-0001-000000000004"),
            ["position"] = new OpenApiString("Engineer"),
            ["lineManagerEmail"] = new OpenApiNull(),
            ["staffCategoryId"] = new OpenApiString("20000000-0000-0000-0002-000000000002"),
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
