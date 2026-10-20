using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

public class PenaltyWaiveEndpointTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Client(string? role = StaffRoles.Librarian)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                new LoginAccount(Guid.NewGuid(), "librarian@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [role])).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    [Fact]
    public async Task A_librarian_waives_an_active_penalty()
    {
        var penaltyId = Guid.NewGuid();
        var loanId = Guid.NewGuid();
        var penalty = PenaltyRecord.Create(
            penaltyId,
            Members.MariamAvetisyan,
            loanId,
            points: 5,
            reason: "Late return test",
            createdAtUtc: DateTime.UtcNow.AddDays(-5),
            expiresAtUtc: DateTime.UtcNow.AddDays(85)
        );

        await Db(async db => { db.Set<PenaltyRecord>().Add(penalty); return await db.SaveChangesAsync(); });

        using var client = Client();
        var request = new WaivePenaltyRequest { Reason = "Manager approved waiver" };

        var response = await client.PostAsJsonAsync($"/api/loans/penalties/{penaltyId}/waive", request, Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var status = await Db(db => db.Set<PenaltyRecord>().Where(p => p.Id == penaltyId).Select(p => p.Status).SingleAsync());
        Assert.Equal(PenaltyRecordStatus.Waived, status);
    }
}