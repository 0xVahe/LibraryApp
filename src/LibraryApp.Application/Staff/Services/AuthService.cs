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

        var account = await accounts.VerifyCredentialsAsync(email, request.Password ?? string.Empty, ct);
        if (account is null)
            return null;

        // MEM-02: only staff members log in. Every token then has staff_member_id (MEM-08, Borrowing).
        // Same 401 as a wrong password, so the answer does not reveal that the account exists.
        if (account.StaffMemberId is null)
            return null;

        // Restricted and Suspended members may still log in to see their standing (MEM-08).
        if (account.StaffStatus == StaffMemberStatus.Deactivated)
            throw new DomainException(StaffErrors.AccountDeactivated);

        var token = tokens.Issue(account);
        return new LoginResponse(token.Token, "Bearer", token.ExpiresAtUtc, account.Email,
            account.MustChangePassword ? [] : account.Roles,
            account.MustChangePassword);
    }

    public Task<bool> ChangeInitialPasswordAsync(Guid userId, ChangeInitialPasswordRequest request,
        CancellationToken ct = default) =>
        accounts.ChangeInitialPasswordAsync(userId, request.CurrentPassword ?? string.Empty,
            request.NewPassword ?? string.Empty, ct);
}
