using LibraryApp.Application.Staff.Services;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Staff;

public class CorporateEmailPolicyTests
{
    private static CorporateEmailPolicy Policy(string domain = "ameriabank.am") =>
        new(Options.Create(new CorporateEmailOptions { Domain = domain }));

    [Theory]
    [InlineData("anna.petrosyan@ameriabank.am")]
    [InlineData("Anna.Petrosyan@AMERIABANK.AM")]
    [InlineData("  anna@ameriabank.am  ")]
    public void Accepts_the_corporate_domain(string email) =>
        Assert.True(Policy().IsCorporate(email));

    [Theory]
    [InlineData("anna@gmail.com")]
    [InlineData("anna@ameriabank.am.evil.com")]
    [InlineData("anna@evil-ameriabank.am")]
    [InlineData("anna@sub.ameriabank.am")]
    [InlineData("@ameriabank.am")]
    [InlineData("anna@")]
    [InlineData("ameriabank.am")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_everything_else(string? email) =>
        Assert.False(Policy().IsCorporate(email));

    [Fact]
    public void Accepts_a_domain_configured_with_a_leading_at_sign() =>
        Assert.True(Policy("@ameriabank.am").IsCorporate("anna@ameriabank.am"));

    [Fact]
    public void Uses_the_configured_domain() =>
        Assert.True(Policy("test.local").IsCorporate("anna@test.local"));
}