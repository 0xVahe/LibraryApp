using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class DepartmentEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/departments";

    private HttpClient Client(string? role = StaffRoles.LibraryAdministrator)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                new LoginAccount(Guid.NewGuid(), "test.admin@ameriabank.am",
                    Guid.NewGuid(),
                    StaffMemberStatus.Active,
                    [role]));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        return client;
    }

    private static string NewName() => $"Department {Guid.NewGuid():N}";

    private async Task<DepartmentDto> Create(string name)
    {
        var response = await Client().PostAsJsonAsync(Route, new UpsertDepartmentRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DepartmentDto>())!;
    }

    private async Task<JsonElement> AssertError(
        HttpResponseMessage responseMessage,
        HttpStatusCode status,
        string expectedErrorCode
    )
    {
        Assert.Equal(status, responseMessage.StatusCode);
        Assert.Equal("application/problem+json", responseMessage.Content.Headers.ContentType!.MediaType);

        var body = await responseMessage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedErrorCode, body.GetProperty("code").GetString());
        return body;
    }

    [Fact]
    public async Task Admin_creates_reads_lists_and_renames_department()
    {
        var client = Client();
        var name = NewName();

        var created = await client.PostAsJsonAsync(Route, new UpsertDepartmentRequest(name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        
        var department = (await created.Content.ReadFromJsonAsync<DepartmentDto>())!;
        Assert.Equal(name, department.Name);
        Assert.Equal(department, await client.GetFromJsonAsync<DepartmentDto>(created.Headers.Location));
        Assert.Contains(department, (await client.GetFromJsonAsync<List<DepartmentDto>>(Route))!);
        
        var renamed = NewName();
        var updated = await client.PutAsJsonAsync($"{Route}/{department.Id}", new UpsertDepartmentRequest(renamed));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(new DepartmentDto(department.Id, renamed), await updated.Content.ReadFromJsonAsync<DepartmentDto>());
        Assert.Equal(renamed, (await client.GetFromJsonAsync<DepartmentDto>($"{Route}/{department.Id}"))!.Name);
    }
    
    [Fact]
    public async Task Duplicate_name_returns_409_on_create_and_update_and_keeps_the_old_name()
    {
        var taken = await Create(NewName());
        var other = await Create(NewName());
        var client = Client();

        // Different case and padding: the check uses the database collation, like the unique index.
        await AssertError(await client.PostAsJsonAsync(Route, new UpsertDepartmentRequest($" {taken.Name.ToUpperInvariant()} ")),
            HttpStatusCode.Conflict, "staff.department_name_taken");
        await AssertError(await client.PutAsJsonAsync($"{Route}/{other.Id}", new UpsertDepartmentRequest(taken.Name)),
            HttpStatusCode.Conflict, "staff.department_name_taken");
        Assert.Equal(other, await client.GetFromJsonAsync<DepartmentDto>($"{Route}/{other.Id}"));
    }

    [Fact]
    public async Task Unknown_id_returns_404()
    {
        var client = Client();
        var route = $"{Route}/{Guid.NewGuid()}";

        await AssertError(await client.GetAsync(route), HttpStatusCode.NotFound, "staff.department_not_found");
        await AssertError(await client.PutAsJsonAsync(route, new UpsertDepartmentRequest(NewName())),
            HttpStatusCode.NotFound, "staff.department_not_found");
    }

    [Fact]
    public async Task Blank_or_too_long_name_returns_400_and_a_padded_100_character_name_is_accepted()
    {
        var client = Client();
        foreach (var name in new[] { null, "   ", new string('a', 101) })
        {
            var body = await AssertError(await client.PostAsJsonAsync(Route, new UpsertDepartmentRequest(name)),
                HttpStatusCode.BadRequest, "validation_failed");
            Assert.Contains(body.GetProperty("errors").EnumerateObject(),
                x => x.Name.Equals(nameof(UpsertDepartmentRequest.Name), StringComparison.OrdinalIgnoreCase));
        }

        var longest = Guid.NewGuid().ToString("N").PadRight(100, 'x');
        Assert.Equal(longest, (await Create($"  {longest}  ")).Name);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Only_a_library_administrator_may_use_the_endpoints(string? role, HttpStatusCode expected)
    {
        var client = Client(role);
        var item = $"{Route}/{Guid.NewGuid()}";
        var request = new UpsertDepartmentRequest(NewName());

        Assert.Equal(expected, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(expected, (await client.GetAsync(item)).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync(Route, request)).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync(item, request)).StatusCode);
    }
}