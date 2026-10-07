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
        var account = new LoginAccount(Guid.NewGuid(), email, status is null ? null : Guid.NewGuid(), status, [role]);
        _accounts[email] = account;
        return account;
    }

    public Task<LoginAccount?> FindByEmailAsync(string email, CancellationToken ct)
    {
        LookedUpEmails.Add(email);
        return Task.FromResult(_accounts.GetValueOrDefault(email));
    }

    public Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken ct) =>
        Task.FromResult(_accounts.Values.Any(a => a.UserId == userId) && password == Password);
}