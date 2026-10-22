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

    [Fact]
    public void Attaches_the_calendar_item_as_a_calendar_file()
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n";
        var message = new OutlookMessage("anna@ameriabank.am", null, "Return your book", "<p>Hello</p>",
            [new OutlookAttachment("due-date.ics", "text/calendar; method=PUBLISH; charset=utf-8", ics)]);

        var mime = OutlookMimeMessage.Create(message, Options);

        var attachment = Assert.IsAssignableFrom<MimePart>(Assert.Single(mime.Attachments));   // text/calendar is read back as a TextPart
        Assert.Equal("due-date.ics", attachment.FileName);
        Assert.Equal("text/calendar", attachment.ContentType.MimeType);
        Assert.Equal("PUBLISH", attachment.ContentType.Parameters["method"]);
        using var content = new MemoryStream();
        attachment.Content.DecodeTo(content);
        Assert.Equal(ics, System.Text.Encoding.UTF8.GetString(content.ToArray()));
        Assert.Equal("<p>Hello</p>", mime.HtmlBody);   // the body is still there
    }

    [Fact]
    public void No_attachment_when_there_is_none()
    {
        Assert.Empty(OutlookMimeMessage.Create(new OutlookMessage("anna@ameriabank.am", null, "S", "<p>B</p>"), Options).Attachments);
        Assert.Empty(OutlookMimeMessage.Create(new OutlookMessage("anna@ameriabank.am", null, "S", "<p>B</p>", []), Options).Attachments);
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
