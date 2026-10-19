using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class MyLibraryEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/staff-members/me";

    private HttpClient Client(string? role, Guid? staffMemberId)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role) with { StaffMemberId = staffMemberId });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        return client;
    }

    private async Task<StaffBorrowingSummary> SummaryAsync(Guid member)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBorrowingReadService>().GetStaffSummaryAsync(member);
    }

    [Theory]
    [InlineData("20000000-0000-0000-0003-000000000003")]   // Anna: loans, an overdue loan, penalties
    [InlineData("20000000-0000-0000-0003-000000000004")]   // Davit
    public async Task StaffMember_sees_only_their_own_data(string id)
    {
        var member = Guid.Parse(id);
        var expected = await SummaryAsync(member);

        var response = await Client(StaffRoles.StaffMember, member).GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<MyLibraryDto>())!;
        Assert.Equal(member, body.Profile.Id);
        Assert.Equal(expected.ActiveLoans.Select(x => x.LoanId), body.ActiveLoans.Select(x => x.LoanId));
        Assert.Equal(expected.Reservations.Select(x => x.ReservationId), body.Reservations.Select(x => x.ReservationId));
        Assert.Equal(expected.ActivePenalties.Select(x => x.PenaltyId), body.ActivePenalties.Select(x => x.PenaltyId));
        Assert.Equal(expected.ActivePenaltyPoints, body.ActivePenaltyPoints);
        Assert.All(body.ActiveLoans, x => Assert.False(string.IsNullOrEmpty(x.Title)));
        Assert.All(body.ActiveLoans, x => Assert.False(string.IsNullOrEmpty(x.Barcode)));
    }

    [Fact]
    public async Task Seeded_overdue_loan_is_flagged()
    {
        var body = (await Client(StaffRoles.StaffMember, Members.AnnaPetrosyan).GetFromJsonAsync<MyLibraryDto>(Route))!;

        Assert.Contains(body.ActiveLoans, x => x.IsOverdue);
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Librarian_and_Administrator_get_403(string role)
    {
        var response = await Client(role, Members.AramSargsyan).GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_token_gets_401()
    {
        var response = await Client(null, null).GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StaffMember_updates_only_their_own_first_and_last_name()
    {
        var memberId = Members.AnnaPetrosyan;
        using var client = Client(StaffRoles.StaffMember, memberId);
        var original = (await client.GetFromJsonAsync<MyLibraryDto>(Route))!.Profile;

        try
        {
            using var response = await client.PutAsJsonAsync(Route, new
            {
                firstName = "  Anna Updated  ",
                lastName = "New Lastname",
                employeeId = "OTHER-EMPLOYEE",
                corporateEmail = "other@ameriabank.am",
                departmentId = Guid.NewGuid(),
                position = "Other position",
                staffCategoryId = Guid.NewGuid(),
                role = StaffRoles.LibraryAdministrator,
                status = "Deactivated",
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<MyProfileDto>())!;
            Assert.Equal("Anna Updated", updated.FirstName);
            Assert.Equal("New Lastname", updated.LastName);
            Assert.Equal(original.CorporateEmail, updated.CorporateEmail);
            Assert.Equal(original.DepartmentName, updated.DepartmentName);
            Assert.Equal(original.Position, updated.Position);
            Assert.Equal(original.StaffCategoryName, updated.StaffCategoryName);
            Assert.Equal(original.Status, updated.Status);
            Assert.Equal(memberId, updated.Id);

            var persisted = (await client.GetFromJsonAsync<MyLibraryDto>(Route))!.Profile;
            Assert.Equal(updated.FirstName, persisted.FirstName);
            Assert.Equal(updated.LastName, persisted.LastName);
        }
        finally
        {
            using var restore = await client.PutAsJsonAsync(Route,
                new UpdateMyProfileRequest(original.FirstName, original.LastName));
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }
    }

    [Theory]
    [InlineData("", "Valid Last", "FirstName")]
    [InlineData("Valid First", "", "LastName")]
    public async Task Invalid_empty_name_returns_400_with_field_error(string firstName, string lastName, string field)
    {
        using var response = await Client(StaffRoles.StaffMember, Members.AnnaPetrosyan)
            .PutAsJsonAsync(Route, new UpdateMyProfileRequest(firstName, lastName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Too_long_name_returns_400_with_field_error()
    {
        using var response = await Client(StaffRoles.StaffMember, Members.AnnaPetrosyan)
            .PutAsJsonAsync(Route, new UpdateMyProfileRequest(new string('A', 101), "Valid Last"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("FirstName", out _));
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Librarian_and_Administrator_cannot_update_self_profile(string role)
    {
        using var response = await Client(role, Members.AnnaPetrosyan)
            .PutAsJsonAsync(Route, new UpdateMyProfileRequest("New First", "New Last"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_token_cannot_update_self_profile()
    {
        using var response = await Client(null, null)
            .PutAsJsonAsync(Route, new UpdateMyProfileRequest("New First", "New Last"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_a_staff_member_claim_gets_404_staff_not_found()
    {
        await AssertNotFound(await Client(StaffRoles.StaffMember, null).GetAsync(Route));
    }

    [Fact]
    public async Task Token_for_an_unknown_member_gets_404_staff_not_found()
    {
        await AssertNotFound(await Client(StaffRoles.StaffMember, Guid.NewGuid()).GetAsync(Route));
    }

    [Fact]
    public async Task Swagger_documents_the_endpoint_with_an_example()
    {
        var json = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        var get = json.GetProperty("paths").GetProperty(Route).GetProperty("get");
        foreach (var status in new[] { "200", "401", "403", "404" })
            Assert.True(get.GetProperty("responses").TryGetProperty(status, out _));
        Assert.True(json.GetProperty("components").GetProperty("schemas").GetProperty(nameof(MyLibraryDto))
            .TryGetProperty("example", out _));

        var put = json.GetProperty("paths").GetProperty(Route).GetProperty("put");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "422" })
            Assert.True(put.GetProperty("responses").TryGetProperty(status, out _));
        var requestSchema = put.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString()!.Split('/').Last();
        Assert.True(json.GetProperty("components").GetProperty("schemas").GetProperty(requestSchema)
            .TryGetProperty("example", out _));
    }

    private static async Task AssertNotFound(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StaffErrors.NotFound.Code, body.GetProperty("code").GetString());
    }
}
