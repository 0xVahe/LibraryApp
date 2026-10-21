using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Domain.Notifications;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>NOT-04 page: real UI cookies and SQL Server; every role sees only its own in-app notifications (NFR-03).</summary>
public class NotificationsPageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/notifications";
    private const string Anna = "anna.petrosyan@ameriabank.am";
    private static readonly DateTime Base = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Signed_out_goes_to_login()
    {
        using var client = UiTestClient.Anonymous(factory);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Lists_own_in_app_unread_first()
    {
        string unread = await AddAsync(Members.AnnaPetrosyan, minute: 1);
        string read = await AddAsync(Members.AnnaPetrosyan, minute: 2, read: true);
        string email = await AddAsync(Members.AnnaPetrosyan, minute: 3, channel: NotificationChannel.Outlook);
        string other = await AddAsync(Members.DavitHakobyan, minute: 4);

        var (response, html, _) = await GetAsync(await CookieAsync(Anna), Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(unread, html);
        Assert.Contains(read, html);
        Assert.DoesNotContain(email, html);
        Assert.DoesNotContain(other, html);
        Assert.True(html.IndexOf(unread, StringComparison.Ordinal) < html.IndexOf(read, StringComparison.Ordinal));
        Assert.Contains("aria-current=\"page\">Notifications", html);
        Assert.DoesNotContain("style=\"", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unread_tab_hides_read()
    {
        string unread = await AddAsync(Members.AnnaPetrosyan, minute: 5);
        string read = await AddAsync(Members.AnnaPetrosyan, minute: 6, read: true);

        var (_, html, _) = await GetAsync(await CookieAsync(Anna), Route + "?unread=true");

        Assert.Contains(unread, html);
        Assert.DoesNotContain(read, html);
    }

    [Fact]
    public async Task Mark_as_read_saves_and_returns()
    {
        string subject = await AddAsync(Members.AnnaPetrosyan, minute: 7);
        Guid id = await IdOfAsync(subject);

        using var response = await PostReadAsync(await CookieAsync(Anna), id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Route, response.Headers.Location?.ToString());
        Assert.NotNull(await ReadAtAsync(id));
    }

    [Fact]
    public async Task Others_notification_is_not_found()
    {
        string subject = await AddAsync(Members.DavitHakobyan, minute: 8);
        Guid id = await IdOfAsync(subject);

        using var response = await PostReadAsync(await CookieAsync(Anna), id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await ReadAtAsync(id));
    }

    private Task<string> CookieAsync(string email) => UiTestClient.CookieAsync(factory, email, StaffSeedData.Password);

    private async Task<(HttpResponseMessage Response, string Html, string SetCookies)> GetAsync(string cookies, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookies);
        var response = await UiTestClient.Anonymous(factory).SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync(), UiTestClient.SetCookies(response));
    }

    /// <summary>Posts the "Mark as read" form like a browser: the antiforgery token and cookie come from the page.</summary>
    private async Task<HttpResponseMessage> PostReadAsync(string auth, Guid id)
    {
        var (_, html, set) = await GetAsync(auth, Route);
        string token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var post = new HttpRequestMessage(HttpMethod.Post, Route + "?handler=Read")
        {
            Content = new FormUrlEncodedContent([
                new("id", id.ToString()), new("unread", "false"), new("page", "1"), new("__RequestVerificationToken", token)]),
        };
        post.Headers.Add("Cookie", $"{auth}; {set}");
        return await UiTestClient.Anonymous(factory).SendAsync(post);
    }

    /// <summary>Adds an in-app (or other channel) notification with a unique subject and returns the subject.</summary>
    private async Task<string> AddAsync(
        Guid staffMemberId, int minute, NotificationChannel channel = NotificationChannel.InApp, bool read = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var created = Base.AddMinutes(minute);
        string subject = $"Page test {Guid.NewGuid():N}";
        var notification = Notification.Create(
            Guid.NewGuid(), staffMemberId, NotificationType.LoanConfirmation, channel,
            "reader@ameriabank.am", null, subject, "<p>Body</p>", created, DateOnly.FromDateTime(created));
        if (read)
            notification.MarkRead(created.AddHours(1));
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return subject;
    }

    private async Task<Guid> IdOfAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        return await db.Notifications.Where(n => n.Subject == subject).Select(n => n.Id).SingleAsync();
    }

    private async Task<DateTime?> ReadAtAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        return await db.Notifications.AsNoTracking().Where(n => n.Id == id).Select(n => n.ReadAtUtc).SingleAsync();
    }
}
