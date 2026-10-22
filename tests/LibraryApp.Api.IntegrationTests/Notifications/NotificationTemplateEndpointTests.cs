using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Notifications;

/// <summary>
/// Administration of the notification templates (NOT-09) on the real host and database with the default templates of the
/// migrations. Tests that activate a template use a notification type of their own, so they do not depend on each other.
/// </summary>
public class NotificationTemplateEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/notification-templates";

    // As the API writes JSON: camelCase, enums as names.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Client(string? role = StaffRoles.LibraryAdministrator)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(role)).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static Task<HttpResponseMessage> Post<T>(HttpClient client, string url, T body) => client.PostAsJsonAsync(url, body, Json);

    private async Task<NotificationTemplateDto> CreateAsync(NotificationType type, string subject = "Subject", string body = "<p>Body</p>")
    {
        using var client = Client();
        var response = await Post(client, Route, new CreateNotificationTemplateRequest(type, subject, body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationTemplateDto>(Json))!;
    }

    private async Task<List<NotificationTemplateDto>> ListAsync(NotificationType? type = null)
    {
        using var client = Client();
        var url = type is null ? Route : $"{Route}?type={type}";
        return (await client.GetFromJsonAsync<List<NotificationTemplateDto>>(url, Json))!;
    }

    // The records hold a list, which a record compares by reference: compare the values.
    private static void AssertSame(NotificationTemplateDto expected, NotificationTemplateDto actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Subject, actual.Subject);
        Assert.Equal(expected.BodyTemplate, actual.BodyTemplate);
        Assert.Equal(expected.IsActive, actual.IsActive);
        Assert.Equal(expected.AllowedPlaceholders, actual.AllowedPlaceholders);
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Only_the_library_administrator_may_use_the_template_endpoints(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        var request = new PreviewNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>B</p>");

        Assert.Equal(expected, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(expected, (await Post(client, Route, new CreateNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>B</p>"))).StatusCode);
        Assert.Equal(expected, (await Post(client, $"{Route}/preview", request)).StatusCode);
        Assert.Equal(expected, (await client.DeleteAsync($"{Route}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task The_list_shows_the_default_template_of_every_type_with_the_placeholders_it_may_use()
    {
        var all = await ListAsync();

        foreach (var type in Enum.GetValues<NotificationType>())
        {
            var active = Assert.Single(all, t => t.Type == type && t.IsActive);
            Assert.Contains("EmployeeName", active.AllowedPlaceholders);
        }

        var welcome = await ListAsync(NotificationType.Welcome);
        Assert.All(welcome, t => Assert.Equal(NotificationType.Welcome, t.Type));
        Assert.Equal(["EmployeeName"], welcome[0].AllowedPlaceholders);
    }

    [Fact]
    public async Task A_template_is_created_inactive_read_back_and_listed()
    {
        var created = await CreateAsync(NotificationType.DueSoonReminder, "Due {{DueDate}}", "<p>{{BookTitle}} is due.</p>");

        Assert.False(created.IsActive);
        using var client = Client();
        AssertSame(created, (await client.GetFromJsonAsync<NotificationTemplateDto>($"{Route}/{created.Id}", Json))!);
        AssertSame(created, Assert.Single(await ListAsync(NotificationType.DueSoonReminder), t => t.Id == created.Id));
    }

    [Fact]
    public async Task Blank_fields_an_unknown_type_and_a_long_subject_are_400_with_errors_per_field()
    {
        using var client = Client();

        foreach (var (type, subject, body, field) in new (NotificationType?, string?, string?, string)[]
                 {
                     (null, "S", "B", "Type"),
                     (NotificationType.Welcome, "  ", "B", "Subject"),
                     (NotificationType.Welcome, new string('a', 301), "B", "Subject"),
                     (NotificationType.Welcome, "S", null, "BodyTemplate"),
                 })
        {
            var body400 = await AssertProblem(await Post(client, Route, new CreateNotificationTemplateRequest(type, subject, body)),
                HttpStatusCode.BadRequest, "validation_failed");
            Assert.Contains(body400.GetProperty("errors").EnumerateObject(), x => x.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task A_placeholder_that_has_no_value_for_the_type_is_400_and_names_it()
    {
        using var client = Client();

        var problem = await AssertProblem(
            await Post(client, Route, new CreateNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>{{BookTitle}} {{Nope}}</p>")),
            HttpStatusCode.BadRequest, "notifications.template_unknown_placeholder");

        var names = problem.GetProperty("placeholders").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(["BookTitle", "Nope"], names);
        Assert.Equal(["EmployeeName"], problem.GetProperty("allowed").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task A_malformed_placeholder_is_400()
    {
        using var client = Client();

        await AssertProblem(
            await Post(client, Route, new CreateNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>{{EmployeeName</p>")),
            HttpStatusCode.BadRequest, "notifications.template_malformed_placeholder");
    }

    [Fact]
    public async Task A_template_is_updated_and_a_bad_text_is_refused_without_changing_it()
    {
        var created = await CreateAsync(NotificationType.OverdueNotice, "Old", "<p>Old</p>");
        using var client = Client();

        var ok = await client.PutAsJsonAsync($"{Route}/{created.Id}", new UpdateNotificationTemplateRequest("Overdue {{BookTitle}}", "<p>Due {{DueDate}}</p>"), Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Overdue {{BookTitle}}", (await ok.Content.ReadFromJsonAsync<NotificationTemplateDto>(Json))!.Subject);

        await AssertProblem(
            await client.PutAsJsonAsync($"{Route}/{created.Id}", new UpdateNotificationTemplateRequest("S", "<p>{{Points}}</p>"), Json),
            HttpStatusCode.BadRequest, "notifications.template_unknown_placeholder");
        Assert.Equal("Overdue {{BookTitle}}", (await client.GetFromJsonAsync<NotificationTemplateDto>($"{Route}/{created.Id}", Json))!.Subject);
    }

    [Fact]
    public async Task Activating_a_template_makes_it_the_only_active_one_of_its_type()
    {
        var before = Assert.Single(await ListAsync(NotificationType.AccessSuspended), t => t.IsActive);
        var created = await CreateAsync(NotificationType.AccessSuspended, "Suspended until {{EffectiveUntil}}", "<p>{{Reason}}</p>");
        using var client = Client();

        var response = await client.PostAsync($"{Route}/{created.Id}/activate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<NotificationTemplateDto>(Json))!.IsActive);
        var after = await ListAsync(NotificationType.AccessSuspended);
        Assert.Equal(created.Id, Assert.Single(after, t => t.IsActive).Id);
        Assert.False(after.Single(t => t.Id == before.Id).IsActive);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{Route}/{created.Id}/activate", null)).StatusCode);   // repeating is safe
        Assert.Single(await ListAsync(NotificationType.AccessSuspended), t => t.IsActive);
    }

    [Fact]
    public async Task An_inactive_template_is_deleted_but_the_active_one_is_422()
    {
        var created = await CreateAsync(NotificationType.PenaltyApplied, "Penalty {{Points}}", "<p>{{TotalPoints}}</p>");
        var active = Assert.Single(await ListAsync(NotificationType.PenaltyApplied), t => t.IsActive);
        using var client = Client();

        await AssertProblem(await client.DeleteAsync($"{Route}/{active.Id}"), HttpStatusCode.UnprocessableEntity,
            "notifications.template_active_cannot_be_deleted");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Route}/{created.Id}")).StatusCode);
        await AssertProblem(await client.GetAsync($"{Route}/{created.Id}"), HttpStatusCode.NotFound, "notifications.template_does_not_exist");
        Assert.Contains(await ListAsync(NotificationType.PenaltyApplied), t => t.Id == active.Id && t.IsActive);
    }

    [Fact]
    public async Task An_unknown_id_is_404_for_every_operation()
    {
        using var client = Client();
        var url = $"{Route}/{Guid.NewGuid()}";

        await AssertProblem(await client.GetAsync(url), HttpStatusCode.NotFound, "notifications.template_does_not_exist");
        await AssertProblem(await client.PutAsJsonAsync(url, new UpdateNotificationTemplateRequest("S", "<p>B</p>"), Json),
            HttpStatusCode.NotFound, "notifications.template_does_not_exist");
        await AssertProblem(await client.PostAsync($"{url}/activate", null), HttpStatusCode.NotFound, "notifications.template_does_not_exist");
        await AssertProblem(await client.DeleteAsync(url), HttpStatusCode.NotFound, "notifications.template_does_not_exist");
    }

    [Fact]
    public async Task The_preview_renders_the_text_with_example_values_and_stores_nothing()
    {
        using var client = Client();
        var before = (await ListAsync()).Count;

        var response = await Post(client, $"{Route}/preview", new PreviewNotificationTemplateRequest(
            NotificationType.ReservationReady, "\"{{BookTitle}}\" is ready", "<p>Dear {{EmployeeName}}, collect it by {{ReadyUntil}}.</p>"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<NotificationTemplatePreviewDto>(Json))!;
        Assert.Equal("\"Clean Code\" is ready", preview.Subject);
        Assert.Contains("Dear Anna Petrosyan, collect it by 16.11.2026.", preview.HtmlBody);
        Assert.Contains("Ameriabank Corporate Library", preview.HtmlBody);   // the email layout
        Assert.Equal(before, (await ListAsync()).Count);
    }

    [Fact]
    public async Task The_preview_refuses_a_placeholder_the_type_does_not_have()
    {
        using var client = Client();

        await AssertProblem(
            await Post(client, $"{Route}/preview", new PreviewNotificationTemplateRequest(NotificationType.Welcome, "S", "<p>{{DueDate}}</p>")),
            HttpStatusCode.BadRequest, "notifications.template_unknown_placeholder");
    }
}
