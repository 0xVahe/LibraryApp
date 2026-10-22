using System.Net;
using System.Net.Sockets;
using LibraryApp.Application.Abstractions.Notifications;
using LibraryApp.Infrastructure.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

public class SmtpOutlookNotificationSenderTests
{
    private const string Recipient = "anna@ameriabank.am";
    private static readonly OutlookMessage Message = new(Recipient, "boss@ameriabank.am", "Your book is due", "<p>Hello Anna</p>");

    private readonly ListLogger _logger = new();

    private SmtpOutlookNotificationSender Sender(int port) =>
        new(Options.Create(new MailOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 }), _logger);

    [Fact]
    public async Task Sends_message()
    {
        await using var server = FakeSmtpServer.Start();

        await Sender(server.Port).SendAsync(Message);

        var data = await server.ReceivedData;
        Assert.Contains("Subject: Your book is due", data);
        Assert.Contains($"To: {Recipient}", data);
        Assert.Contains("Cc: boss@ameriabank.am", data);
        Assert.Contains("RCPT TO:<boss@ameriabank.am>", server.Commands);
        Assert.Empty(_logger.Messages);
    }

    [Fact]
    public async Task Sends_the_calendar_item_as_an_attachment()
    {
        await using var server = FakeSmtpServer.Start();
        var message = Message with
        {
            Attachments = [new OutlookAttachment("due-date.ics", "text/calendar; method=PUBLISH; charset=utf-8", "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n")],
        };

        await Sender(server.Port).SendAsync(message);

        var data = await server.ReceivedData;
        Assert.Contains("multipart/mixed", data);
        Assert.Contains("Content-Type: text/calendar", data);
        Assert.Contains("due-date.ics", data);
    }

    [Fact]
    public async Task Failed_quit_still_counts_as_sent()
    {
        await using var server = FakeSmtpServer.Start(failQuit: true);

        await Sender(server.Port).SendAsync(Message);

        Assert.Contains("Subject: Your book is due", await server.ReceivedData);
        Assert.Contains("QUIT", server.Commands);
        Assert.All(_logger.Messages, AssertNoAddresses);
    }

    [Fact]
    public async Task Unreachable_server_fails_without_addresses()
    {
        var port = FreePort();

        var ex = await Assert.ThrowsAsync<NotificationDeliveryException>(() => Sender(port).SendAsync(Message));

        Assert.Contains($"127.0.0.1:{port}", ex.Message);
        AssertNoAddresses(ex.Message);
        Assert.Empty(_logger.Messages);
    }

    [Fact]
    public async Task Rejected_recipient_fails_with_smtp_code()
    {
        await using var server = FakeSmtpServer.Start(rejectRecipients: true);

        var ex = await Assert.ThrowsAsync<NotificationDeliveryException>(() => Sender(server.Port).SendAsync(Message));

        Assert.Contains("SMTP 550", ex.Message);
        AssertNoAddresses(ex.Message);
        Assert.Empty(_logger.Messages);
    }

    [Fact]
    public async Task Invalid_address_fails_before_connecting()
    {
        var ex = await Assert.ThrowsAsync<NotificationDeliveryException>(() =>
            Sender(FreePort()).SendAsync(Message with { To = "anna@" }));

        Assert.IsType<MimeKit.ParseException>(ex.InnerException);
        Assert.DoesNotContain("anna@", ex.Message);
    }

    [Fact]
    public async Task Cancellation_is_not_a_delivery_failure()
    {
        await using var server = FakeSmtpServer.Start();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Sender(server.Port).SendAsync(Message, new CancellationToken(canceled: true)));
        Assert.Empty(_logger.Messages);
    }

    // NFR-06: emails are personal data.
    private static void AssertNoAddresses(string text)
    {
        Assert.DoesNotContain(Recipient, text);
        Assert.DoesNotContain("boss@ameriabank.am", text);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ListLogger : ILogger<SmtpOutlookNotificationSender>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
