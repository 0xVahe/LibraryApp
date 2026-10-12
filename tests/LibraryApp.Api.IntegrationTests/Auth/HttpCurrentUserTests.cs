using System.Security.Claims;
using System.Security.Cryptography;
using LibraryApp.Api.Auth;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Api.IntegrationTests.Auth;

/// <summary>The real ICurrentUser over a principal from a token of JwtTokenIssuer, validated with the app's bearer options.</summary>
public class HttpCurrentUserTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid StaffMemberId = Guid.NewGuid();
    private const string Email = "anna@ameriabank.am";

    [Fact]
    public async Task Reads_every_member_from_an_issued_token()
    {
        var user = await FromIssuedToken(StaffMemberId);

        Assert.Equal(UserId, user.UserId);
        Assert.Equal(StaffMemberId, user.StaffMemberId);
        Assert.Equal(Email, user.Email);
        Assert.Equal(StaffRoles.Librarian, user.Role);
        Assert.True(user.IsInRole(StaffRoles.Librarian));
        Assert.False(user.IsInRole(StaffRoles.LibraryAdministrator));
    }

    [Fact]
    public async Task StaffMemberId_is_null_when_the_token_has_no_staff_member_claim()
    {
        var user = await FromIssuedToken(staffMemberId: null);

        Assert.Null(user.StaffMemberId);
        Assert.Equal(UserId, user.UserId);
    }

    [Theory]
    [InlineData(nameof(ICurrentUser.UserId))]
    [InlineData(nameof(ICurrentUser.StaffMemberId))]
    [InlineData(nameof(ICurrentUser.Email))]
    [InlineData(nameof(ICurrentUser.Role))]
    [InlineData(nameof(ICurrentUser.IsInRole))]
    public void Every_member_throws_without_an_authenticated_user(string member)
    {
        var anonymous = For(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) });
        var outsideRequest = For(null);

        Assert.Throws<InvalidOperationException>(() => Read(anonymous, member));
        Assert.Throws<InvalidOperationException>(() => Read(outsideRequest, member));
    }

    [Fact]
    public void A_missing_required_claim_throws()
    {
        var user = For(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("Bearer")) });

        Assert.Throws<InvalidOperationException>(() => user.UserId);
    }

    private static async Task<ICurrentUser> FromIssuedToken(Guid? staffMemberId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "LibraryApp",
            ["Jwt:Audience"] = "LibraryApp.Api",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock>(new NowClock());
        services.AddLibraryJwtAuthentication(configuration);
        using var provider = services.BuildServiceProvider();

        var token = provider.GetRequiredService<IAccessTokenIssuer>().Issue(
            new LoginAccount(UserId, Email, staffMemberId, StaffMemberStatus.Active, [StaffRoles.Librarian]));
        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, bearer.TokenValidationParameters);
        Assert.True(result.IsValid);

        return For(new DefaultHttpContext { User = new ClaimsPrincipal(result.ClaimsIdentity) });
    }

    private static ICurrentUser For(HttpContext? context) => new HttpCurrentUser(new TestHttpContextAccessor(context));
    private static object? Read(ICurrentUser user, string member) => member switch
    {
        nameof(ICurrentUser.UserId) => user.UserId,
        nameof(ICurrentUser.StaffMemberId) => user.StaffMemberId,
        nameof(ICurrentUser.Email) => user.Email,
        nameof(ICurrentUser.Role) => user.Role,
        _ => user.IsInRole(StaffRoles.Librarian)
    };

    private sealed class NowClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly LocalToday => DateOnly.FromDateTime(UtcNow);
        public DateTime ToLocal(DateTime utc) => utc;
        public DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(utc);
        public DateTime StartOfLocalDayUtc(DateOnly localDate) => localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        public DateTime EndOfLocalDayUtc(DateOnly localDate) => StartOfLocalDayUtc(localDate.AddDays(1)).AddTicks(-1);
    }
    
    /// <summary>Holds the context in a field: HttpContextAccessor uses AsyncLocal, which does not flow out of an async helper.</summary>
    private sealed class TestHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}

