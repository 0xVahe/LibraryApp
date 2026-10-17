using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>SD-14 on every list endpoint: a page far past the end is an empty page, not an error.</summary>
public class PagingTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Theory]
    [InlineData("/api/books")]
    [InlineData("/api/staff-members")]
    public async Task The_last_possible_page_number_is_an_empty_page(string path)
    {
        var librarian = StaffSeedData.People.Single(p => p.StaffMemberId == StaffSeedIds.StaffMembers.AramSargsyan).Email;
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = librarian, password = StaffSeedData.Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"{path}?page={int.MaxValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }
}
