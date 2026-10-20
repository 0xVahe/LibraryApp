using LibraryApp.Application.Abstractions.Notifications;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LibraryApp.Infrastructure.Mail;

/// <summary>Sends notification emails over SMTP with MailKit (NOT-03). In training the server is smtp4dev.</summary>
public sealed class SmtpOutlookNotificationSender(
    IOptions<MailOptions> options,
    ILogger<SmtpOutlookNotificationSender> logger) : IOutlookNotificationSender
{
    public async Task SendAsync(OutlookMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        MimeMessage mime;
        try
        {
            mime = OutlookMimeMessage.Create(message, settings);
        }
        catch (ParseException ex)
        {
            throw new NotificationDeliveryException("A recipient address is not a valid email address.", ex);
        }

        using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, ct);

            if (!string.IsNullOrEmpty(settings.UserName))
                await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, ct);

            await client.SendAsync(mime, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Server and error kind only: MailKit messages can contain the recipient address (NFR-06).
            // Not logged here: the caller logs the failure once, with the notification id.
            var reason = $"Mail server {settings.Host}:{settings.Port} did not accept the message ({Describe(ex)}).";
            throw new NotificationDeliveryException(reason, ex);
        }

        // The server has accepted the message: a failed QUIT must not turn into a retry and a duplicate email.
        try
        {
            await client.DisconnectAsync(quit: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Notification email sent, but the connection to {Host}:{Port} did not close cleanly ({Error}).",
                settings.Host, settings.Port, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        SmtpCommandException smtp => $"SMTP {(int)smtp.StatusCode}, {smtp.ErrorCode}",
        SmtpProtocolException => "SMTP protocol error",
        _ => ex.GetType().Name,
    };
}
