using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

public class LoanHistoryEndpointTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
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

    [Fact]
    public async Task Get_loan_history_returns_paginated_results()
    {
        using var client = Client();

        // Act
        var response = await client.GetAsync("/api/loans/history?page=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<LoanDto>>(Json);

        Assert.NotNull(result);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task Get_loan_history_filtered_by_staff_member()
    {
        using var client = Client();

        // Act
        var response = await client.GetAsync($"/api/loans/history?staffMemberId={Members.MariamAvetisyan}&page=1&pageSize=5");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<LoanDto>>(Json);

        Assert.NotNull(result);
        Assert.All(result.Items, loan => Assert.Equal(Members.MariamAvetisyan, loan.StaffMemberId));
    }
}