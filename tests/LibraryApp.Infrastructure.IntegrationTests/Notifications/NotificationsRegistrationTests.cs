using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Mail;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

public class NotificationsRegistrationTests
{
    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationsModule(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Without_a_mail_section_the_sender_uses_smtp4dev_defaults()
    {
        using var services = Services(new());

        Assert.IsType<SmtpOutlookNotificationSender>(services.GetRequiredService<IOutlookNotificationSender>());
        var options = services.GetRequiredService<IOptions<MailOptions>>().Value;
        Assert.Equal("localhost", options.Host);
        Assert.Equal(2525, options.Port);
        Assert.Equal(SecureSocketOptions.None, options.Security);
    }

    [Fact]
    public void Mail_section_is_bound()
    {
        using var services = Services(new()
        {
            ["Mail:Host"] = "smtp.example.am",
            ["Mail:Port"] = "587",
            ["Mail:Security"] = "StartTls",
        });

        var options = services.GetRequiredService<IOptions<MailOptions>>().Value;
        Assert.Equal("smtp.example.am", options.Host);
        Assert.Equal(587, options.Port);
        Assert.Equal(SecureSocketOptions.StartTls, options.Security);
    }

    [Theory]
    [InlineData("Mail:Port", "0")]
    [InlineData("Mail:Host", "")]
    [InlineData("Mail:FromAddress", "not-an-email")]
    [InlineData("Mail:TimeoutSeconds", "0")]
    public void Invalid_mail_settings_fail_options_validation(string key, string value)
    {
        using var services = Services(new() { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MailOptions>>().Value);
    }

    [Theory]
    [InlineData("Mail:UserName", "library")]
    [InlineData("Mail:Password", "secret-value")]
    public void Half_credentials_fail_validation(string key, string value)
    {
        using var services = Services(new() { [key] = value });

        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MailOptions>>().Value);
        Assert.Contains("Mail:UserName and Mail:Password", ex.Message);
        Assert.DoesNotContain(value, ex.Message);
    }

    [Fact]
    public void Both_credentials_are_valid()
    {
        using var services = Services(new() { ["Mail:UserName"] = "library", ["Mail:Password"] = "secret-value" });

        var options = services.GetRequiredService<IOptions<MailOptions>>().Value;
        Assert.Equal("library", options.UserName);
    }
}
