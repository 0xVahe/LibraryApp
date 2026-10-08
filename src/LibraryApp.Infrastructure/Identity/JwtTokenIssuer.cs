using System.Security.Claims;
using System.Text;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Ports;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LibraryApp.Infrastructure.Identity;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock) : IAccessTokenIssuer
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(LoginAccount account)
    {
        var now = clock.UtcNow;
        var expiresAtUtc = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (account.StaffMemberId is { } staffMemberId)
            claims.Add(new Claim(LibraryClaimTypes.StaffMemberId, staffMemberId.ToString()));
        claims.AddRange(account.Roles.Select(role => new Claim(LibraryClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });

        return new AccessToken(token, expiresAtUtc);
    }
}