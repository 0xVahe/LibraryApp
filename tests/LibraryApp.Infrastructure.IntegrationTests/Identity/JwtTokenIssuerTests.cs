using System.Security.Cryptography;
using System.Text;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

public class JwtTokenIssuerTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private static readonly JwtOptions Settings = new()
    {
        Issuer = "LibraryApp",
        Audience = "LibraryApp.Api",
        SigningKey = NewKey(),
        AccessTokenMinutes = 60
    };
    
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static readonly LoginAccount Account = new(
        Guid.NewGuid(), "anna.petrosyan@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active,
        [StaffRoles.Librarian, StaffRoles.LibraryAdministrator]);

    private static JwtTokenIssuer Issuer() => new(Options.Create(Settings), new FixedClock(Now));

    private static TokenValidationParameters Validation(string signingKey) => new()
    {
        ValidIssuer = Settings.Issuer,
        ValidAudience = Settings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = false
    };

    [Fact]
    public void Token_carries_user_staff_member_and_roles()
    {
        var token = new JsonWebToken(Issuer().Issue(Account).Token);

        Assert.Equal(Account.UserId.ToString(), token.Subject);
        Assert.Equal(Account.Email, token.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(Account.StaffMemberId.ToString(), token.GetClaim(LibraryClaimTypes.StaffMemberId).Value);
        Assert.Equal(Account.Roles, token.Claims.Where(c => c.Type == LibraryClaimTypes.Role).Select(c => c.Value));
        Assert.Equal(Settings.Issuer, token.Issuer);
        Assert.Contains(Settings.Audience, token.Audiences);
    }

    [Fact]
    public void Token_expires_after_the_configured_minutes()
    {
        var issued = Issuer().Issue(Account);

        Assert.Equal(Now.AddMinutes(60), issued.ExpiresAtUtc);
        Assert.Equal(Now.AddMinutes(60), new JsonWebToken(issued.Token).ValidTo);
    }

    [Fact]
    public void Account_without_staff_member_has_no_staff_member_claim()
    {
        var token = new JsonWebToken(Issuer().Issue(Account with { StaffMemberId = null, StaffStatus = null }).Token);

        Assert.False(token.TryGetClaim(LibraryClaimTypes.StaffMemberId, out _));
    }

    [Fact]
    public void Password_change_token_keeps_staff_member_claim_but_omits_roles()
    {
        var token = new JsonWebToken(Issuer().Issue(Account with { MustChangePassword = true }).Token);

        Assert.Equal(Account.StaffMemberId.ToString(), token.GetClaim(LibraryClaimTypes.StaffMemberId).Value);
        Assert.Equal(bool.TrueString, token.GetClaim(LibraryClaimTypes.PasswordChangeRequired).Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Type == LibraryClaimTypes.Role);
    }

    [Fact]
    public async Task Token_is_valid_with_the_same_key()
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(Issuer().Issue(Account).Token, Validation(Settings.SigningKey));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Token_is_rejected_with_another_key()
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(Issuer().Issue(Account).Token, Validation(NewKey()));

        Assert.False(result.IsValid);
    }
}
