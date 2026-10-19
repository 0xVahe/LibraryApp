using System.Security.Cryptography;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

public class JwtRegistrationTests
{
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["Jwt:Issuer"] = "LibraryApp",
        ["Jwt:Audience"] = "LibraryApp.Api",
        ["Jwt:SecurityStampCacheSeconds"] = "30",
        ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock>(new FixedClock(DateTime.UtcNow));
        services.AddLibraryJwtAuthentication(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Bearer_options_accept_a_token_from_the_issuer_and_read_roles()
    {
        using var services = Services(ValidSettings);
        var token = services.GetRequiredService<IAccessTokenIssuer>().Issue(new LoginAccount(
            Guid.NewGuid(), "anna@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [StaffRoles.Librarian], "test-stamp"));
        var bearer = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, bearer.TokenValidationParameters);

        Assert.True(result.IsValid);
        Assert.Equal(LibraryClaimTypes.Role, bearer.TokenValidationParameters.RoleClaimType);
        Assert.True(result.ClaimsIdentity.HasClaim(LibraryClaimTypes.Role, StaffRoles.Librarian));
    }

    [Fact]
    public void Missing_signing_key_fails_options_validation()
    {
        using var services = Services(new()
        {
            ["Jwt:Issuer"] = "LibraryApp",
            ["Jwt:Audience"] = "LibraryApp.Api"
        });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JwtOptions>>().Value);
    }

    [Fact]
    public void Missing_cache_duration_uses_the_thirty_second_default()
    {
        var settings = new Dictionary<string, string?>(ValidSettings);
        settings.Remove("Jwt:SecurityStampCacheSeconds");
        using var services = Services(settings);

        Assert.Equal(30, services.GetRequiredService<IOptions<JwtOptions>>().Value.SecurityStampCacheSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("31")]
    public void Invalid_cache_duration_fails_options_validation(string seconds)
    {
        var settings = new Dictionary<string, string?>(ValidSettings)
        {
            ["Jwt:SecurityStampCacheSeconds"] = seconds
        };
        using var services = Services(settings);

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JwtOptions>>().Value);
    }
}
