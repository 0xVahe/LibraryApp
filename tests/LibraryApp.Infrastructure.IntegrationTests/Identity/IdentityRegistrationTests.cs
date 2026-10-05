using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

public class IdentityRegistrationTests
{
    private static ServiceProvider Services() => new ServiceCollection().AddLibraryIdentity().BuildServiceProvider();

    [Fact]
    public void Password_rules_match_BR_MEM_5()
    {
        using var services = Services();
        var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(8, options.Password.RequiredLength);
        Assert.True(options.Password.RequireDigit);
        Assert.False(options.Password.RequireUppercase);
        Assert.False(options.Password.RequireLowercase);
        Assert.False(options.Password.RequireNonAlphanumeric);
        Assert.True(options.User.RequireUniqueEmail);
    }

    [Fact]
    public void Letter_rule_is_registered()
    {
        using var services = Services();

        Assert.Contains(services.GetServices<IPasswordValidator<AppUser>>(), v => v is LetterPasswordValidator);
    }
}