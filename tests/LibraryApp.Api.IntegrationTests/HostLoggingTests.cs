namespace LibraryApp.Api.IntegrationTests;

/// <summary>
/// Regression: test hosts run in one process. When a host set the process-wide <see cref="Serilog.Log.Logger"/>, the
/// next host replaced it and the first host to stop closed it for everybody — the file and console logs of the other
/// hosts stopped without a trace. Each host must write only to its own logger (preserveStaticLogger in Program.cs).
/// </summary>
public class HostLoggingTests
{
    [Fact]
    public async Task Starting_and_stopping_a_host_leaves_the_process_wide_logger_alone()
    {
        Serilog.ILogger before = Serilog.Log.Logger;
        var host = new ApiHostFactory();
        await host.InitializeAsync();

        try
        {
            await host.CreateClient().GetAsync("/health/live");   // a request: the host logs through its own logger

            Assert.Same(before, Serilog.Log.Logger);
        }
        finally
        {
            await host.DisposeAsync();
        }

        Assert.Same(before, Serilog.Log.Logger);
    }
}
