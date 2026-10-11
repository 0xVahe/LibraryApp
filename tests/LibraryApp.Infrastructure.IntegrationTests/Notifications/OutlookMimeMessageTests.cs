using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Infrastructure.Mail;
using MimeKit;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

public class OutlookMimeMessageTests
{
    private static readonly MailOptions Options = new() { FromAddress = "library@ameriabank.am", FromName = "Ameriabank Library" };

    [Fact]
    public void Builds_message()
    {
        var message = new OutlookMessage("anna@ameriabank.am", "boss@ameriabank.am", "Your book is due", "<p>Hello Anna</p>");

        var mime = OutlookMimeMessage.Create(message, Options);

        var from = Assert.Single(mime.From.Mailboxes);
        Assert.Equal("library@ameriabank.am", from.Address);
        Assert.Equal("Ameriabank Library", from.Name);
        Assert.Equal("anna@ameriabank.am", Assert.Single(mime.To.Mailboxes).Address);
        Assert.Equal("boss@ameriabank.am", Assert.Single(mime.Cc.Mailboxes).Address);
        Assert.Equal("Your book is due", mime.Subject);
        Assert.Equal("<p>Hello Anna</p>", mime.HtmlBody);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_cc_when_cc_is_empty(string? cc)
    {
        var mime = OutlookMimeMessage.Create(new OutlookMessage("anna@ameriabank.am", cc, "Subject", "<p>Body</p>"), Options);

        Assert.Empty(mime.Cc);
    }

    [Fact]
    public void Invalid_recipient_address_is_rejected()
    {
        Assert.Throws<ParseException>(() =>
            OutlookMimeMessage.Create(new OutlookMessage("anna@", null, "Subject", "<p>Body</p>"), Options));
    }
}
