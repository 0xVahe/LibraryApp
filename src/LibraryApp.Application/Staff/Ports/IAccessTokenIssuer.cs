namespace LibraryApp.Application.Staff.Ports;

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

public interface IAccessTokenIssuer
{
    AccessToken Issue(LoginAccount account);
}