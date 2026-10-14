using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>API host with the Staff demo accounts, so the tests log in as real seeded users.</summary>
public sealed class AuthFactory : ApiHostFactory, IAsyncLifetime
{
    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        using var scope = Services.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().OfType<StaffSeeder>().Single();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(seeder.SeedAsync);
    }
}

public class AuthEndpointTests(AuthFactory factory) : IClassFixture<AuthFactory>
{
    private const string Password = "Passw0rd1";

    private Task<HttpResponseMessage> Login(string email, string password = Password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task Gmail_address_returns_400()
    {
        var response = await Login("anna@gmail.com");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false, "application/json")]
    [InlineData(false, "text/plain")]
    [InlineData(true, "application/json")]
    [InlineData(true, "text/plain")]
    public async Task Invalid_credentials_return_the_same_401_problem(bool existingAccount, string accept)
    {
        var email = $"login.{Guid.NewGuid():N}@ameriabank.am";
        if (existingAccount)
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPass1" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(401, body.GetProperty("status").GetInt32());
        Assert.Equal("staff.invalid_credentials", body.GetProperty("code").GetString());
        Assert.Equal("urn:library:error:staff.invalid_credentials", body.GetProperty("type").GetString());
        Assert.Equal(StaffErrors.InvalidCredentials.Message, body.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("lilit.grigoryan@ameriabank.am", StaffRoles.LibraryAdministrator)]
    [InlineData("aram.sargsyan@ameriabank.am", StaffRoles.Librarian)]
    [InlineData("anna.petrosyan@ameriabank.am", StaffRoles.StaffMember)]
    public async Task Seeded_accounts_return_a_token_with_the_role(string email, string role)
    {
        var response = await Login(email, StaffSeedData.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("Bearer", body!.TokenType);
        Assert.Equal(role, Assert.Single(body.Roles));
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Account_without_a_staff_profile_returns_401_invalid_credentials(string role)
    {
        var email = $"no-profile.{Guid.NewGuid():N}@ameriabank.am";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }

        var response = await Login(email);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StaffErrors.InvalidCredentials.Code, body.GetProperty("code").GetString());
    }
}
