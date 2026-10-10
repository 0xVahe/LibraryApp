using System.Security.Claims;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Api.Auth;

/// <summary>Reads the claims issued by JwtTokenIssuer (MapInboundClaims is off, so the names are as issued).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => Guid.Parse(Required(JwtRegisteredClaimNames.Sub));

    public Guid? StaffMemberId =>
        Principal.FindFirst(LibraryClaimTypes.StaffMemberId)?.Value is { } id ? Guid.Parse(id) : null;

    public string Email => Required(JwtRegisteredClaimNames.Email);

    public string Role => Required(LibraryClaimTypes.Role);

    public bool IsInRole(string role) => Principal.IsInRole(role);   // RoleClaimType is LibraryClaimTypes.Role

    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new InvalidOperationException("There is no authenticated user in the current request.");

    private string Required(string claimType) =>
        Principal.FindFirst(claimType)?.Value
        ?? throw new InvalidOperationException($"The access token has no '{claimType}' claim.");
}