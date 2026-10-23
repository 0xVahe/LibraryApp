using LibraryApp.Application.Abstractions.Notifications;
using MimeKit;

namespace LibraryApp.Infrastructure.Mail;

/// <summary>Builds the email from an <see cref="OutlookMessage"/>; apart from sending, so it is tested without a server.</summary>
public static class OutlookMimeMessage
{
    /// <exception cref="ParseException">To or Cc is not a valid address.</exception>
    public static MimeMessage Create(OutlookMessage message, MailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));

        if (!string.IsNullOrWhiteSpace(message.Cc))
            mime.Cc.Add(MailboxAddress.Parse(message.Cc));

        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody };
        foreach (var attachment in message.Attachments ?? [])
            body.Attachments.Add(attachment.FileName, System.Text.Encoding.UTF8.GetBytes(attachment.Text), ContentType.Parse(attachment.MediaType));

        mime.Body = body.ToMessageBody();
        return mime;
    }
}
