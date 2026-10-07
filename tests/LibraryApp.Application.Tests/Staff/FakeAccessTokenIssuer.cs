using LibraryApp.Application.Staff.Ports;

namespace LibraryApp.Application.Tests.Staff;

internal sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public static readonly DateTime ExpiresAtUtc = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);

    public List<LoginAccount> Issued { get; } = [];

    public AccessToken Issue(LoginAccount account)
    {
        Issued.Add(account);
        return new AccessToken($"token-for-{account.UserId}", ExpiresAtUtc);
    }
}