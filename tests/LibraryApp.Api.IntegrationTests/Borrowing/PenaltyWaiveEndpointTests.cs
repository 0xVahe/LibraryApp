using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// BOR-08 through HTTP on SQL Server: finding a staff member's penalty records and waiving one with a mandatory
/// reason; the waiver recalculates the status at once, with <c>StaffStatusChanged</c> only on a real change. Every test
/// uses its own staff member, so the tests of the class do not share records.
/// </summary>
public class PenaltyWaiveEndpointTests(BorrowingDailyJobHost factory) : IClassFixture<BorrowingDailyJobHost>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Waiving_the_record_that_kept_a_member_restricted_makes_them_active_with_one_event()
    {
        Guid member = Members.NarekSimonyan;
        PenaltyRecord record = await SeedRecordAsync(member, points: 5, StaffMemberStatus.Restricted);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        using HttpResponseMessage response = await librarian.PostAsJsonAsync(
            $"/api/penalties/{record.Id}/waive", new WaivePenaltyRequest("  Book found in the office  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PenaltyDto body = (await response.Content.ReadFromJsonAsync<PenaltyDto>(Json))!;
        Assert.Equal((PenaltyRecordStatus.Waived, "Book found in the office"), (body.Status, body.WaivedReason));

        Assert.Equal(PenaltyRecordStatus.Waived, (await ReadRecordAsync(record.Id)).Status);
        Assert.Equal(StaffMemberStatus.Active, await StatusAsync(member));

        StaffStatusChanged changed = Assert.Single(await StatusEventsAsync(member));
        Assert.Equal((StaffMemberStatus.Restricted, StaffMemberStatus.Active), (changed.OldStatus, changed.NewStatus));
    }

    [Fact]
    public async Task Waiving_the_last_active_record_of_a_suspended_member_changes_the_status_at_once()
    {
        // The daily job looks only at members with Active records: the waiver itself must leave the right status.
        Guid member = Members.EvaHovsepyan;
        PenaltyRecord record = await SeedRecordAsync(member, points: 10, StaffMemberStatus.Suspended);

        using HttpClient admin = Client(StaffRoles.LibraryAdministrator);
        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/penalties/{record.Id}/waive", new WaivePenaltyRequest("Returned by the line manager"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaffMemberStatus.Active, await StatusAsync(member));
        Assert.Single(await StatusEventsAsync(member));
    }

    [Fact]
    public async Task A_waiver_that_leaves_the_status_unchanged_publishes_no_status_event()
    {
        Guid member = Members.TigranMelikyan;
        PenaltyRecord record = await SeedRecordAsync(member, points: 1, StaffMemberStatus.Active);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        Assert.Equal(HttpStatusCode.OK, (await librarian.PostAsJsonAsync(
            $"/api/penalties/{record.Id}/waive", new WaivePenaltyRequest("Courtesy"))).StatusCode);

        Assert.Equal(StaffMemberStatus.Active, await StatusAsync(member));
        Assert.Empty(await StatusEventsAsync(member));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_waiver_without_a_reason_is_refused_with_400(string? reason)
    {
        PenaltyRecord record = await SeedRecordAsync(Members.ManeGevorgyan, points: 2, StaffMemberStatus.Active);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        using HttpResponseMessage response = await librarian.PostAsJsonAsync(
            $"/api/penalties/{record.Id}/waive", new { reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PenaltyRecordStatus.Active, (await ReadRecordAsync(record.Id)).Status);
    }

    [Fact]
    public async Task Unknown_already_waived_and_forbidden_requests_are_refused()
    {
        PenaltyRecord record = await SeedRecordAsync(Members.DavitHakobyan, points: 3, StaffMemberStatus.Active);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        await AssertProblemAsync(await librarian.PostAsJsonAsync(
                $"/api/penalties/{Guid.NewGuid()}/waive", new WaivePenaltyRequest("Reason")),
            HttpStatusCode.NotFound, BorrowingErrors.PenaltyNotFound.Code);

        Assert.Equal(HttpStatusCode.OK, (await librarian.PostAsJsonAsync(
            $"/api/penalties/{record.Id}/waive", new WaivePenaltyRequest("First"))).StatusCode);
        await AssertProblemAsync(await librarian.PostAsJsonAsync(
                $"/api/penalties/{record.Id}/waive", new WaivePenaltyRequest("Second")),
            HttpStatusCode.UnprocessableEntity, BorrowingErrors.PenaltyNotActive.Code);
        Assert.Equal("First", (await ReadRecordAsync(record.Id)).WaivedReason);

        // A Staff Member sees their own points through My library; the penalty endpoints are for library staff.
        using HttpClient member = Client(StaffRoles.StaffMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(
            $"/api/penalties/{record.Id}/waive", new WaivePenaltyRequest("Mine"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.GetAsync($"/api/penalties?staffMemberId={Members.DavitHakobyan}")).StatusCode);
    }

    [Fact]
    public async Task The_records_of_a_staff_member_are_listed_newest_first_and_filtered_by_status()
    {
        Guid member = Members.MariamAvetisyan;
        PenaltyRecord older = await SeedRecordAsync(member, points: 1, StaffMemberStatus.Active, createdDaysAgo: 3);
        PenaltyRecord newer = await SeedRecordAsync(member, points: 2, StaffMemberStatus.Active, createdDaysAgo: 1);

        using HttpClient librarian = Client(StaffRoles.Librarian);
        Assert.Equal(HttpStatusCode.OK, (await librarian.PostAsJsonAsync(
            $"/api/penalties/{older.Id}/waive", new WaivePenaltyRequest("Courtesy"))).StatusCode);

        PagedResult<PenaltyDto> all = (await librarian.GetFromJsonAsync<PagedResult<PenaltyDto>>(
            $"/api/penalties?staffMemberId={member}", Json))!;
        Assert.Equal(new[] { newer.Id, older.Id }, all.Items.Select(item => item.PenaltyId));
        Assert.Equal((2, 1, PageRequest.DefaultPageSize), (all.TotalCount, all.Page, all.PageSize));

        PagedResult<PenaltyDto> active = (await librarian.GetFromJsonAsync<PagedResult<PenaltyDto>>(
            $"/api/penalties?staffMemberId={member}&status=Active", Json))!;
        Assert.Equal(newer.Id, Assert.Single(active.Items).PenaltyId);
    }

    private DateTime Now => factory.Services.GetRequiredService<IClock>().UtcNow;

    /// <summary>A record on a returned loan of the member, with the member put into <paramref name="status"/>.</summary>
    private async Task<PenaltyRecord> SeedRecordAsync(
        Guid staffMemberId,
        int points,
        StaffMemberStatus status,
        int createdDaysAgo = 1)
    {
        DateTime created = Now.AddDays(-createdDaysAgo);
        Loan loan = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(40, 1), staffMemberId, created.AddDays(-30), 14);
        loan.Return(created);
        PenaltyRecord record = PenaltyRecord.Create(
            Guid.NewGuid(), staffMemberId, loan.Id, points, "Returned late", created, created.AddDays(90));

        await Db(async db =>
        {
            StaffMember member = await db.StaffMembers.SingleAsync(m => m.Id == staffMemberId);
            if (member.Status != status)
                member.ChangeStatus(status);
            db.Loans.Add(loan);
            db.PenaltyRecords.Add(record);
            return await db.SaveChangesAsync();
        });

        return record;
    }

    private Task<PenaltyRecord> ReadRecordAsync(Guid id) =>
        Db(db => db.PenaltyRecords.AsNoTracking().SingleAsync(record => record.Id == id));

    private Task<StaffMemberStatus> StatusAsync(Guid staffMemberId) =>
        Db(db => db.StaffMembers.AsNoTracking().Where(m => m.Id == staffMemberId).Select(m => m.Status).SingleAsync());

    private async Task<List<StaffStatusChanged>> StatusEventsAsync(Guid staffMemberId) =>
        (await Db(db => db.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == nameof(StaffStatusChanged))
            .Select(message => message.Payload)
            .ToListAsync()))
        .Select(payload => JsonSerializer.Deserialize<StaffStatusChanged>(payload, Json)!)
        .Where(changed => changed.StaffMemberId == staffMemberId)
        .ToList();

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    private HttpClient Client(string role)
    {
        HttpClient client = factory.CreateClient();
        string token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(factory.Account(role)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
