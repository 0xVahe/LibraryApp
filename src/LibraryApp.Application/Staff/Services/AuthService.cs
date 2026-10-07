using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

/// <summary>MEM-02 login: corporate email + password → JWT.</summary>
public sealed class AuthService(
    IUserAccountStore accounts,
    IAccessTokenIssuer tokens,
    CorporateEmailPolicy emailPolicy) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        // Also checked by LoginRequestValidator; repeated here so no caller can skip MEM-03.
        var email = request.Email?.Trim() ?? string.Empty;
        if (!emailPolicy.IsCorporate(email))
            throw new DomainException(StaffErrors.EmailNotCorporate);

        var account = await accounts.FindByEmailAsync(email, ct);
        if (account is null || !await accounts.CheckPasswordAsync(account.UserId, request.Password ?? string.Empty, ct))
            return null;

        // Restricted and Suspended members may still log in to see their standing (MEM-08).
        if (account.StaffStatus == StaffMemberStatus.Deactivated)
            throw new DomainException(StaffErrors.AccountDeactivated);

        var token = tokens.Issue(account);
        return new LoginResponse(token.Token, "Bearer", token.ExpiresAtUtc, account.Email, account.Roles);
    }
}