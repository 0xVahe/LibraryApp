using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>Minimal SMTP server on a free local port: accepts one session and keeps what it received.</summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly bool _rejectRecipients;
    private readonly bool _failQuit;
    private readonly TaskCompletionSource<string> _data = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _session;

    private FakeSmtpServer(bool rejectRecipients, bool failQuit)
    {
        _rejectRecipients = rejectRecipients;
        _failQuit = failQuit;
        _listener.Start();
        _session = Task.Run(RunAsync);
    }

    /// <param name="failQuit">Answers QUIT with an error and drops the connection, after the message was accepted.</param>
    public static FakeSmtpServer Start(bool rejectRecipients = false, bool failQuit = false) => new(rejectRecipients, failQuit);

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Commands { get; } = [];

    /// <summary>The message text sent after DATA.</summary>
    public Task<string> ReceivedData => _data.Task.WaitAsync(TimeSpan.FromSeconds(10));

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                Commands.Add(line);
                var command = line.Split(' ')[0].ToUpperInvariant();
                switch (command)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync("250 fake");
                        break;
                    case "RCPT" when _rejectRecipients:
                        await writer.WriteLineAsync("550 5.1.1 Mailbox unavailable");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        _data.TrySetResult(await ReadDataAsync(reader));
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "QUIT" when _failQuit:
                        await writer.WriteLineAsync("421 4.4.2 Connection dropped");
                        return;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The client closed the connection or the test ended.
        }
        finally
        {
            _data.TrySetCanceled();
        }
    }

    private static async Task<string> ReadDataAsync(StreamReader reader)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line && line != ".")
            data.AppendLine(line);
        return data.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await _session.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
    }
}
