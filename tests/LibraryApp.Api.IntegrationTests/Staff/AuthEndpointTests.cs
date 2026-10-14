using System.Net;
using System.Net.Http.Headers;
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
    [InlineData(Password, HttpStatusCode.Forbidden, "staff.account_deactivated")]
    [InlineData("WrongPass1", HttpStatusCode.Unauthorized, "staff.invalid_credentials")]
    public async Task Deactivated_account_checks_the_password_before_revealing_its_status(
        string password, HttpStatusCode status, string code)
    {
        using var administrator = await AdministratorClient();
        var member = await Enrol(administrator);
        using var initialLogin = await Login(member.CorporateEmail);
        Assert.Equal(HttpStatusCode.OK, initialLogin.StatusCode);

        using var deactivation = await administrator.PostAsJsonAsync(
            $"/api/staff-members/{member.Id}/deactivate", new DeactivateStaffMemberRequest("Employee left the bank."));
        Assert.Equal(HttpStatusCode.NoContent, deactivation.StatusCode);

        using var response = await Login(member.CorporateEmail, password);

        await AssertLoginError(response, status, code);
    }

    [Fact]
    public async Task Wrong_passwords_lock_the_account_at_the_configured_limit_and_reject_the_right_password()
    {
        using var administrator = await AdministratorClient();
        var member = await Enrol(administrator);
        using var initialLogin = await Login(member.CorporateEmail);
        Assert.Equal(HttpStatusCode.OK, initialLogin.StatusCode);

        using var settingsScope = factory.Services.CreateScope();
        var maxAttempts = settingsScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .Options.Lockout.MaxFailedAccessAttempts;
        Assert.True(maxAttempts > 0);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await Login(member.CorporateEmail, "WrongPass1");
            await AssertLoginError(response, HttpStatusCode.Unauthorized, "staff.invalid_credentials");

            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(member.CorporateEmail);
            Assert.NotNull(user);
            Assert.Equal(attempt == maxAttempts, await users.IsLockedOutAsync(user));
        }

        using var lockedLogin = await Login(member.CorporateEmail);
        await AssertLoginError(lockedLogin, HttpStatusCode.Unauthorized, "staff.invalid_credentials");
    }

    private async Task<HttpClient> AdministratorClient()
    {
        var administrator = StaffSeedData.People.Single(x => x.Role == StaffRoles.LibraryAdministrator);
        using var login = await Login(administrator.Email, StaffSeedData.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(token);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static async Task<StaffMemberDto> Enrol(HttpClient administrator)
    {
        var unique = Guid.NewGuid().ToString("N");
        var request = new EnrolStaffMemberRequest($"AUTH-{unique}", "Demo", "Employee",
            $"auth.{unique}@ameriabank.am", StaffSeedIds.Departments.InformationTechnology,
            "Engineer", null, StaffSeedIds.Categories.Employee, Password);
        using var response = await administrator.PostAsJsonAsync("/api/staff-members", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffMemberDto>())!;
    }

    private static async Task AssertLoginError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal($"urn:library:error:{code}", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
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
