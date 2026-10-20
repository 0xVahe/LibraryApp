using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Common;
using LibraryApp.Application.Notifications.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Notifications;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>
/// The in-app notification API (NOT-04, NFR-03) on the real host and database. Every test uses its own staff member, so
/// the notifications of one test never show up in another.
/// </summary>
public class NotificationEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTime Base = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private HttpClient Client(string? role, Guid? staffMemberId = null)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role) with { StaffMemberId = staffMemberId ?? Guid.NewGuid() }).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private HttpClient Reader(Guid staffMemberId) => Client(StaffRoles.StaffMember, staffMemberId);

    /// <summary>Adds a notification for the staff member; <paramref name="minute"/> orders them by creation time.</summary>
    private async Task<Guid> AddAsync(
        Guid staffMemberId,
        int minute = 0,
        NotificationChannel channel = NotificationChannel.InApp,
        bool read = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var created = Base.AddMinutes(minute);
        var notification = Notification.Create(
            Guid.NewGuid(), staffMemberId, NotificationType.LoanConfirmation, channel,
            "reader@ameriabank.am", null, $"Subject {minute}", "<p>Body</p>", created, DateOnly.FromDateTime(created));
        if (read)
            notification.MarkRead(created.AddHours(1));
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    private static async Task<PagedResult<NotificationDto>> PageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<NotificationDto>>(Json))!;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_staff_member_lists_only_their_own_in_app_notifications()
    {
        var own = await AddAsync(Members.AnnaPetrosyan, 1);
        await AddAsync(Members.AnnaPetrosyan, 2, NotificationChannel.Outlook);
        await AddAsync(Members.DavitHakobyan, 3);   // another person's

        using var anna = Reader(Members.AnnaPetrosyan);
        var page = await PageAsync(anna, "/api/notifications/me");

        var item = Assert.Single(page.Items);
        Assert.Equal(own, item.NotificationId);
        Assert.Equal(1, page.TotalCount);
        Assert.False(item.IsRead);
        Assert.Null(item.ReadAtUtc);
        Assert.Equal(Base.AddMinutes(1), item.CreatedAtUtc);
    }

    [Fact]
    public async Task The_list_puts_unread_first_then_newest_and_the_unread_route_returns_only_unread()
    {
        var oldUnread = await AddAsync(Members.DavitHakobyan, 1);
        var newUnread = await AddAsync(Members.DavitHakobyan, 2);
        var read = await AddAsync(Members.DavitHakobyan, 3, read: true);

        using var davit = Reader(Members.DavitHakobyan);
        var all = await PageAsync(davit, "/api/notifications/me");
        var unread = await PageAsync(davit, "/api/notifications/me/unread");

        // The Borrowing seed may already hold a notification for this staff member: look at this test's own ones.
        Guid[] mine = [newUnread, oldUnread, read];
        var ownInAll = all.Items.Where(n => mine.Contains(n.NotificationId)).ToList();
        Assert.Equal(mine, ownInAll.Select(n => n.NotificationId));
        Assert.True(ownInAll[2].IsRead);
        Assert.NotNull(ownInAll[2].ReadAtUtc);
        Assert.Equal(read, all.Items[^1].NotificationId);   // read ones come after every unread one
        Assert.All(unread.Items, n => Assert.False(n.IsRead));
        Assert.Equal([newUnread, oldUnread], unread.Items.Where(n => mine.Contains(n.NotificationId)).Select(n => n.NotificationId));
    }

    [Fact]
    public async Task Paging_is_applied_and_out_of_range_values_are_corrected()
    {
        for (int minute = 1; minute <= 3; minute++)
            await AddAsync(Members.MariamAvetisyan, minute);

        using var mariam = Reader(Members.MariamAvetisyan);
        var second = await PageAsync(mariam, "/api/notifications/me?page=2&pageSize=2");
        var corrected = await PageAsync(mariam, "/api/notifications/me?page=0&pageSize=1000");

        Assert.Single(second.Items);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, second.TotalPages);
        Assert.Equal(1, corrected.Page);
        Assert.Equal(PageRequest.MaxPageSize, corrected.PageSize);
        Assert.Equal(3, corrected.Items.Count);
    }

    [Fact]
    public async Task Marking_as_read_persists_and_repeating_keeps_the_first_time()
    {
        var id = await AddAsync(Members.GorMkrtchyan, 1);
        using var gor = Reader(Members.GorMkrtchyan);

        var first = await gor.PatchAsync($"/api/notifications/{id}/read", null);
        var firstDto = (await first.Content.ReadFromJsonAsync<NotificationDto>(Json))!;
        var second = await gor.PatchAsync($"/api/notifications/{id}/read", null);
        var secondDto = (await second.Content.ReadFromJsonAsync<NotificationDto>(Json))!;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(firstDto.IsRead);
        Assert.NotNull(firstDto.ReadAtUtc);
        Assert.Equal(firstDto.ReadAtUtc, secondDto.ReadAtUtc);

        Assert.Empty((await PageAsync(gor, "/api/notifications/me/unread")).Items);
        var all = await PageAsync(gor, "/api/notifications/me");
        Assert.Equal(firstDto.ReadAtUtc, Assert.Single(all.Items).ReadAtUtc);
    }

    [Fact]
    public async Task Another_persons_notification_and_an_unknown_id_are_404_and_stay_unread()
    {
        var id = await AddAsync(Members.SonaVardanyan, 1);

        using var other = Reader(Members.HaykManukyan);
        await AssertProblem(await other.PatchAsync($"/api/notifications/{id}/read", null),
            HttpStatusCode.NotFound, "notifications.notification_not_found");
        await AssertProblem(await other.PatchAsync($"/api/notifications/{Guid.NewGuid()}/read", null),
            HttpStatusCode.NotFound, "notifications.notification_not_found");

        using var owner = Reader(Members.SonaVardanyan);
        Assert.False(Assert.Single((await PageAsync(owner, "/api/notifications/me")).Items).IsRead);
    }

    [Fact]
    public async Task An_outlook_notification_cannot_be_marked_as_read()
    {
        var id = await AddAsync(Members.LilitGrigoryan, 1, NotificationChannel.Outlook);

        using var lilit = Client(StaffRoles.LibraryAdministrator, Members.LilitGrigoryan);
        await AssertProblem(await lilit.PatchAsync($"/api/notifications/{id}/read", null),
            HttpStatusCode.UnprocessableEntity, "notifications.only_in_app_can_be_read");
    }

    [Fact]
    public async Task A_token_without_a_staff_member_claim_sees_nothing_and_unauthenticated_requests_are_401()
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications/me/unread")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PatchAsync($"/api/notifications/{Guid.NewGuid()}/read", null)).StatusCode);

        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
            factory.Account(StaffRoles.StaffMember) with { StaffMemberId = null }).Token;
        using var noProfile = factory.CreateClient();
        noProfile.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Empty((await PageAsync(noProfile, "/api/notifications/me")).Items);
        await AssertProblem(await noProfile.PatchAsync($"/api/notifications/{Guid.NewGuid()}/read", null),
            HttpStatusCode.NotFound, "notifications.notification_not_found");
    }

    [Fact]
    public async Task A_malformed_id_is_404_by_routing()
    {
        using var anna = Reader(Members.AnnaPetrosyan);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.PatchAsync("/api/notifications/not-a-guid/read", null)).StatusCode);
    }
}
