using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Staff;

public class AuthServiceTests
{
    private readonly FakeUserAccountStore _accounts = new();
    private readonly FakeAccessTokenIssuer _tokens = new();
    private readonly AuthService _service;

    public AuthServiceTests() => _service = new AuthService(
        _accounts, _tokens, new CorporateEmailPolicy(Options.Create(new CorporateEmailOptions())));

    private static LoginRequest Request(string? email, string? password = FakeUserAccountStore.Password) => new(email, password);

    [Fact]
    public async Task Login_returns_a_bearer_token_for_valid_credentials()
    {
        var account = _accounts.Add(role: StaffRoles.Librarian);

        var response = await _service.LoginAsync(Request(account.Email));

        Assert.NotNull(response);
        Assert.Equal($"token-for-{account.UserId}", response.AccessToken);
        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(FakeAccessTokenIssuer.ExpiresAtUtc, response.ExpiresAtUtc);
        Assert.Equal(account.Email, response.Email);
        Assert.Equal(StaffRoles.Librarian, Assert.Single(response.Roles));
    }

    [Theory]
    [InlineData("anna@gmail.com")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Login_rejects_a_non_corporate_email_before_looking_up_the_account(string? email)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(() => _service.LoginAsync(Request(email)));

        Assert.Equal(StaffErrors.EmailNotCorporate, exception.Error);
        Assert.Empty(_accounts.LookedUpEmails);
    }

    [Fact]
    public async Task Login_returns_null_for_an_unknown_email()
    {
        var response = await _service.LoginAsync(Request("nobody@ameriabank.am"));

        Assert.Null(response);
        Assert.Empty(_tokens.Issued);
    }

    [Theory]
    [InlineData("WrongPass1")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Login_returns_null_for_a_wrong_password(string? password)
    {
        var account = _accounts.Add();

        var response = await _service.LoginAsync(Request(account.Email, password));

        Assert.Null(response);
        Assert.Empty(_tokens.Issued);
    }

    [Fact]
    public async Task Login_rejects_a_Deactivated_member_even_with_the_right_password()
    {
        var account = _accounts.Add(status: StaffMemberStatus.Deactivated);

        var exception = await Assert.ThrowsAsync<DomainException>(() => _service.LoginAsync(Request(account.Email)));

        Assert.Equal(StaffErrors.AccountDeactivated, exception.Error);
        Assert.Empty(_tokens.Issued);
    }

    [Theory]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    public async Task Login_allows_Restricted_and_Suspended_members(StaffMemberStatus status)
    {
        var account = _accounts.Add(status: status);

        Assert.NotNull(await _service.LoginAsync(Request(account.Email)));
    }

    [Fact]
    public async Task Login_trims_the_email_before_the_lookup()
    {
        var account = _accounts.Add();

        var response = await _service.LoginAsync(Request($"  {account.Email}  "));

        Assert.NotNull(response);
        Assert.Equal(account.Email, Assert.Single(_accounts.LookedUpEmails));
    }
}