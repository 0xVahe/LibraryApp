using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class TokenRevocationFactory : ApiHostFactory
{
    public CacheClock Clock { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddControllers().AddApplicationPart(typeof(TestAuthorizationController).Assembly);
        services.Configure<MemoryCacheOptions>(options => options.Clock = Clock);
    }

    // Only cache time advances: JWT lifetime validation continues to use real time.
    public sealed class CacheClock : Microsoft.Extensions.Internal.ISystemClock
    {
        private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;
        public DateTimeOffset UtcNow => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
}

/// <summary>Real login, bearer middleware, Identity and SQL Server; no account-validation bypass (MEM-02, NFR-02).</summary>
public class TokenRevocationEndpointTests(TokenRevocationFactory factory) : IClassFixture<TokenRevocationFactory>
{
    private const string Password = "DemoPass1";
    private const string Probe = "/api/test-authorization/default";
    private const string ChangedPassword = "ChangedPass9";

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<LoginResponse> Login(string email, string password = Password)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<HttpClient> Administrator()
    {
        var person = StaffSeedData.People.Single(x => x.Role == StaffRoles.LibraryAdministrator);
        return Client((await Login(person.Email, StaffSeedData.Password)).AccessToken);
    }

    private async Task<StaffMemberDto> Enrol(HttpClient administrator)
    {
        var unique = Guid.NewGuid().ToString("N");
        using var response = await administrator.PostAsJsonAsync("/api/staff-members",
            new EnrolStaffMemberRequest($"TOKEN-{unique}", "Demo", "Employee", $"token.{unique}@ameriabank.am",
                StaffSeedIds.Departments.InformationTechnology, "Engineer", null,
                StaffSeedIds.Categories.Employee, Password));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffMemberDto>())!;
    }

    private static UpdateStaffMemberRequest Update(StaffMemberDto member, string email) =>
        new(member.FirstName, member.LastName, email, member.DepartmentId, member.Position,
            member.LineManagerEmail, member.StaffCategoryId);

    [Theory]
    [InlineData("email")]
    [InlineData("role")]
    [InlineData("deactivation")]
    [InlineData("password")]
    public async Task A_warmed_token_is_rejected_after_the_cache_expires_and_new_credentials_use_fresh_state(string change)
    {
        using var administrator = await Administrator();
        var member = await Enrol(administrator);
        var login = await Login(member.CorporateEmail);
        using var oldClient = Client(login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync(Probe)).StatusCode); // warm the cache
        var email = member.CorporateEmail;
        var password = Password;

        switch (change)
        {
            case "email":
                email = $"changed.{Guid.NewGuid():N}@ameriabank.am";
                using (var response = await administrator.PutAsJsonAsync($"/api/staff-members/{member.Id}", Update(member, email)))
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                break;
            case "role":
                // There is no role-edit endpoint yet. Use the application's registered Identity store.
                using (var scope = factory.Services.CreateScope())
                {
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                    var user = (await users.FindByEmailAsync(email))!;
                    var initialStamp = user.SecurityStamp;
                    Assert.True((await users.RemoveFromRoleAsync(user, StaffRoles.StaffMember)).Succeeded);
                    Assert.NotEqual(initialStamp, user.SecurityStamp);
                    var removedStamp = user.SecurityStamp;
                    Assert.True((await users.AddToRoleAsync(user, StaffRoles.Librarian)).Succeeded);
                    Assert.NotEqual(removedStamp, user.SecurityStamp);
                }
                break;
            case "deactivation":
                using (var response = await administrator.PostAsJsonAsync($"/api/staff-members/{member.Id}/deactivate",
                           new DeactivateStaffMemberRequest("Left the bank")))
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                break;
            case "password":
                password = ChangedPassword;
                using (var response = await oldClient.PostAsJsonAsync("/api/auth/change-password",
                           new ChangePasswordRequest(Password, password)))
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                break;
        }

        if (change == "deactivation")
        {
            using var anonymous = factory.CreateClient();
            using var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            var freshLogin = await Login(email, password);
            var oldStamp = new JsonWebToken(login.AccessToken).GetClaim(LibraryClaimTypes.SecurityStamp).Value;
            var freshStamp = new JsonWebToken(freshLogin.AccessToken).GetClaim(LibraryClaimTypes.SecurityStamp).Value;
            Assert.NotEqual(oldStamp, freshStamp);
            using var freshClient = Client(freshLogin.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await freshClient.GetAsync(Probe)).StatusCode);
            if (change == "role")
                Assert.Equal([StaffRoles.Librarian], freshLogin.Roles);
        }

        // Reads near expiry must not extend the old entry: this is absolute, not sliding, expiration.
        var cacheSeconds = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SecurityStampCacheSeconds;
        factory.Clock.Advance(TimeSpan.FromSeconds(cacheSeconds - 1));
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync(Probe)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(2));
        await AssertUnauthorized(await oldClient.GetAsync(Probe));
    }

    [Theory]
    [InlineData("missing_stamp")]
    [InlineData("wrong_stamp")]
    [InlineData("missing_account")]
    [InlineData("missing_profile")]
    [InlineData("deactivated_status")]
    public async Task Signed_tokens_with_invalid_account_state_are_rejected(string invalidState)
    {
        using var administrator = await Administrator();
        var member = await Enrol(administrator);
        var login = new JsonWebToken((await Login(member.CorporateEmail)).AccessToken);
        var userId = Guid.Parse(login.Subject);
        var stamp = login.GetClaim(LibraryClaimTypes.SecurityStamp).Value;

        if (invalidState == "missing_account")
            userId = Guid.NewGuid();
        if (invalidState == "wrong_stamp")
            stamp = Guid.NewGuid().ToString();
        if (invalidState is "missing_profile" or "deactivated_status")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
            {
                if (invalidState == "missing_profile")
                {
                    var email = $"orphan.{Guid.NewGuid():N}@ameriabank.am";
                    var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email };
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                    Assert.True((await users.CreateAsync(user, Password)).Succeeded);
                    userId = user.Id;
                    stamp = user.SecurityStamp!;
                }
                else
                {
                    // Keep the stamp unchanged to prove the middleware also checks Staff status.
                    (await db.StaffMembers.SingleAsync(x => x.Id == member.Id, ct)).Deactivate();
                }
            });
        }

        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        if (invalidState != "missing_stamp")
            claims.Add(new Claim(LibraryClaimTypes.SecurityStamp, stamp));
        var settings = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256)
        });
        using var client = Client(token);
        await AssertUnauthorized(await client.GetAsync(Probe));
    }

    [Theory]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    public async Task Restricted_and_suspended_members_can_still_authenticate(StaffMemberStatus status)
    {
        using var administrator = await Administrator();
        var member = await Enrol(administrator);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
                (await db.StaffMembers.SingleAsync(x => x.Id == member.Id, ct)).ChangeStatus(status));
        }
        using var client = Client((await Login(member.CorporateEmail)).AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Probe)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rolled_back_email_or_role_changes_preserve_the_token(bool roleChange)
    {
        using var administrator = await Administrator();
        var member = await Enrol(administrator);
        var login = await Login(member.CorporateEmail);
        using var client = Client(login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Probe)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(member.CorporateEmail))!;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
                {
                    if (roleChange)
                        Assert.True((await users.RemoveFromRoleAsync(user, StaffRoles.StaffMember)).Succeeded);
                    else
                        await scope.ServiceProvider.GetRequiredService<IStaffAccountStore>()
                            .UpdateEmailAsync(user.Id, $"rollback.{Guid.NewGuid():N}@ameriabank.am", ct);
                    throw new InvalidOperationException("Simulated transaction failure.");
                }));
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Probe)).StatusCode);
        var freshLogin = await Login(member.CorporateEmail);
        Assert.Equal(new JsonWebToken(login.AccessToken).GetClaim(LibraryClaimTypes.SecurityStamp).Value,
            new JsonWebToken(freshLogin.AccessToken).GetClaim(LibraryClaimTypes.SecurityStamp).Value);
        Assert.Equal([StaffRoles.StaffMember], freshLogin.Roles);
    }

    private static async Task AssertUnauthorized(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("unauthorized", body.GetProperty("code").GetString());
            Assert.Equal("urn:library:error:unauthorized", body.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
            Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
        }
    }
}
