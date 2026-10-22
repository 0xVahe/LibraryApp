using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LibraryApp.Api.OpenApi;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Notifications;

/// <summary>
/// NFR-11 for the notification template endpoints (NOT-09): every request and response schema has an example and a description
/// for every property; the request examples work as they are on the freshly seeded data; the response examples are what the API
/// really answers (the seeded default template, and the preview of the example request).
/// </summary>
public sealed class NotificationSwaggerTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/notification-templates";

    private static readonly string[] Schemas =
    [
        "CreateNotificationTemplateRequest", "UpdateNotificationTemplateRequest", "PreviewNotificationTemplateRequest",
        "NotificationTemplateDto", "NotificationTemplatePreviewDto",
    ];

    private async Task<JsonElement> SchemasAsync()
    {
        using var client = factory.CreateClient();
        return (await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json")).GetProperty("components").GetProperty("schemas");
    }

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(StaffRoles.LibraryAdministrator)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(JsonElement example) => new(example.GetRawText(), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Every_template_schema_has_an_example_and_described_properties()
    {
        var schemas = await SchemasAsync();

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

    [Fact]
    public async Task The_request_examples_work_as_they_are_on_the_seeded_data()
    {
        var schemas = await SchemasAsync();
        JsonElement Example(string name) => schemas.GetProperty(name).GetProperty("example");
        using var client = Client();

        using var created = await client.PostAsync(Route, Body(Example("CreateNotificationTemplateRequest")));
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        using var updated = await client.PutAsync($"{Route}/{id}", Body(Example("UpdateNotificationTemplateRequest")));
        Assert.True(updated.StatusCode == HttpStatusCode.OK, $"update: {(int)updated.StatusCode} {await updated.Content.ReadAsStringAsync()}");

        using var preview = await client.PostAsync($"{Route}/preview", Body(Example("PreviewNotificationTemplateRequest")));
        Assert.True(preview.StatusCode == HttpStatusCode.OK, $"preview: {(int)preview.StatusCode} {await preview.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task The_template_example_is_the_seeded_default_template_as_the_api_returns_it()
    {
        var example = (await SchemasAsync()).GetProperty("NotificationTemplateDto").GetProperty("example");
        using var client = Client();

        var record = await client.GetFromJsonAsync<JsonElement>($"{Route}/{example.GetProperty("id").GetString()}");

        Assert.Equal(JsonSerializer.Serialize(example), JsonSerializer.Serialize(record));
    }

    [Fact]
    public async Task The_preview_example_is_what_the_api_answers_to_the_preview_request_example()
    {
        var schemas = await SchemasAsync();
        using var client = Client();

        using var response = await client.PostAsync($"{Route}/preview", Body(schemas.GetProperty("PreviewNotificationTemplateRequest").GetProperty("example")));

        Assert.Equal(
            JsonSerializer.Serialize(schemas.GetProperty("NotificationTemplatePreviewDto").GetProperty("example")),
            JsonSerializer.Serialize(await response.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public void The_example_text_only_uses_placeholders_of_its_type()
    {
        var used = LibraryApp.Application.Notifications.Services.NotificationPlaceholders
            .Find(NotificationExamples.ExampleSubject + NotificationExamples.ExampleBody);

        Assert.All(used, name => Assert.Contains(name,
            LibraryApp.Application.Notifications.Services.NotificationPlaceholders.AllowedFor(NotificationExamples.ExampleType)));
    }
}
