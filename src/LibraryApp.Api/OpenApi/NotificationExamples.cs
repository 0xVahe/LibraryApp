using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence.Seed.Notifications;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LibraryApp.Api.OpenApi;

/// <summary>
/// Request and response examples of the notification template administration (NOT-09, NFR-11), taken from the Development
/// seed: the requests work on a freshly seeded database, the template example is the seeded default Welcome template, and the
/// preview example is what the renderer really makes of the preview request (checked by <c>NotificationSwaggerTests</c>).
/// </summary>
public sealed class NotificationExamples : ISchemaFilter
{
    /// <summary>The type and the text of the request examples: a new Welcome text that only uses placeholders of its type.</summary>
    public const NotificationType ExampleType = NotificationType.Welcome;
    public const string ExampleSubject = "Welcome to the library, {{EmployeeName}}";
    public const string ExampleBody = "<p>Dear {{EmployeeName}},</p><p>Welcome to the Ameriabank corporate library.</p>";

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        OpenApiObject? example = context.Type switch
        {
            Type type when type == typeof(CreateNotificationTemplateRequest) => new OpenApiObject
            {
                ["type"] = new OpenApiString(ExampleType.ToString()),
                ["subject"] = new OpenApiString(ExampleSubject),
                ["bodyTemplate"] = new OpenApiString(ExampleBody),
            },
            Type type when type == typeof(UpdateNotificationTemplateRequest) => new OpenApiObject
            {
                ["subject"] = new OpenApiString(ExampleSubject),
                ["bodyTemplate"] = new OpenApiString(ExampleBody),
            },
            Type type when type == typeof(PreviewNotificationTemplateRequest) => new OpenApiObject
            {
                ["type"] = new OpenApiString(ExampleType.ToString()),
                ["subject"] = new OpenApiString(ExampleSubject),
                ["bodyTemplate"] = new OpenApiString(ExampleBody),
            },
            Type type when type == typeof(NotificationTemplateDto) => SeededTemplate(),
            Type type when type == typeof(NotificationTemplatePreviewDto) => Preview(),
            _ => null,
        };

        if (example is not null)
            schema.Example = example;
    }

    /// <summary>The default Welcome template of the migrations, as <c>GET /api/notification-templates/{id}</c> returns it.</summary>
    private static OpenApiObject SeededTemplate()
    {
        var seeded = NotificationTemplateSeedData.Templates.Single(template => template.Type == ExampleType);
        var allowed = new OpenApiArray();
        allowed.AddRange(NotificationPlaceholders.AllowedFor(seeded.Type).Select(name => new OpenApiString(name)));

        return new OpenApiObject
        {
            ["id"] = new OpenApiString(seeded.Id.ToString()),
            ["type"] = new OpenApiString(seeded.Type.ToString()),
            ["subject"] = new OpenApiString(seeded.Subject),
            ["bodyTemplate"] = new OpenApiString(seeded.Body),
            ["isActive"] = new OpenApiBoolean(true),
            ["allowedPlaceholders"] = allowed,
        };
    }

    /// <summary>What the preview endpoint answers to the preview request example: the same renderer, the same example values.</summary>
    private static OpenApiObject Preview()
    {
        var values = NotificationPlaceholders.AllowedFor(ExampleType)
            .ToDictionary(name => name, name => NotificationPlaceholders.SampleValues[name]);
        RenderedNotification rendered = new NotificationTemplateRenderer().Render(ExampleSubject, ExampleBody, values);

        return new OpenApiObject
        {
            ["subject"] = new OpenApiString(rendered.Subject),
            ["htmlBody"] = new OpenApiString(rendered.HtmlBody),
        };
    }
}
