using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Staff;

internal sealed class FakeUserAccountStore : IUserAccountStore
{
    public const string Password = "Passw0rd1";

    private readonly Dictionary<string, LoginAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);

    public List<string> LookedUpEmails { get; } = [];

    public LoginAccount Add(
        string email = "anna.petrosyan@ameriabank.am",
        StaffMemberStatus? status = StaffMemberStatus.Active,
        string role = StaffRoles.StaffMember)
    {
        var account = new LoginAccount(Guid.NewGuid(), email, status is null ? null : Guid.NewGuid(), status, [role], "test-stamp");
        _accounts[email] = account;
        return account;
    }

    public Task<LoginAccount?> VerifyCredentialsAsync(string email, string password, CancellationToken ct)
    {
        LookedUpEmails.Add(email);
        return Task.FromResult(password == Password ? _accounts.GetValueOrDefault(email) : null);
    }

    public Task<bool> VerifyCurrentPasswordAsync(Guid userId, string currentPassword, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct) =>
        throw new NotSupportedException();
}
