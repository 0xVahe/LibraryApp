using System.Net.Http.Headers;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Api.Logging;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>NFR-10: every request is logged with user, route, duration and status.</summary>
public class RequestLoggingTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task An_authenticated_request_is_logged_with_the_user_route_duration_and_status()
    {
        // A seeded account: the token carries its security stamp, so it passes the account check of every request.
        LoginAccount account = factory.Account(StaffRoles.Librarian);
        string userId = account.UserId.ToString();
        using var client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(account).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string path = $"/api/books/{Guid.NewGuid()}";   // an unknown book: 404, still one request log

        await client.GetAsync(path);

        LogEvent entry = await RequestLogAsync(path);
        Assert.Equal(userId, Scalar(entry, "UserId"));
        Assert.Equal(404, int.Parse(Scalar(entry, "StatusCode")));
        Assert.True(entry.Properties.ContainsKey("Elapsed"));
        Assert.Equal("GET", Scalar(entry, "RequestMethod"));
    }

    [Fact]
    public async Task A_page_request_with_the_ui_cookie_is_logged_with_the_user()
    {
        LoginAccount account = factory.Account(StaffRoles.Librarian);
        using var client = UiTestClient.Anonymous(factory);
        client.DefaultRequestHeaders.Add("Cookie", await UiTestClient.CookieAsync(factory, account.Email, StaffSeedData.Password));

        await client.GetAsync("/desk");

        LogEvent entry = await RequestLogAsync("/desk");
        Assert.Equal(account.UserId.ToString(), Scalar(entry, "UserId"));
        Assert.Equal(200, int.Parse(Scalar(entry, "StatusCode")));
    }

    [Fact]
    public async Task An_anonymous_request_is_logged_as_anonymous()
    {
        string path = $"/health/live?probe={Guid.NewGuid():N}";

        await factory.CreateClient().GetAsync(path);

        LogEvent entry = await RequestLogAsync("/health/live", e => true);
        Assert.Equal(RequestLogging.Anonymous, Scalar(entry, "UserId"));
    }

    private static async Task<LogEvent> RequestLogAsync(string path, Func<LogEvent, bool>? extra = null)
    {
        // The request log is written when the response completes: allow it a moment.
        for (int attempt = 0; attempt < 50; attempt++)
        {
            LogEvent? found = ApiHostFactory.Logs.Events.LastOrDefault(e =>
                e.MessageTemplate.Text == RequestLogging.MessageTemplate
                && Scalar(e, "RequestPath") == path
                && (extra?.Invoke(e) ?? true));
            if (found is not null)
                return found;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"No request log for {path}.");
    }

    private static string Scalar(LogEvent entry, string name) =>
        entry.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue { Value: { } scalar }
            ? scalar.ToString()!
            : "";
}
