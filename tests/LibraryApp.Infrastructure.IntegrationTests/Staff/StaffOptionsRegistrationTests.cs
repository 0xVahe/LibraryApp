using LibraryApp.Application.Staff.Options;
using LibraryApp.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Staff;

/// <summary>Uses a minimal host, without SQL, to exercise the real module's startup validation.</summary>
public sealed class StaffOptionsRegistrationTests
{
    private static Dictionary<string, string?> Valid() => new()
    {
        ["Staff:Import:MaxRows"] = "3",
        ["Staff:Import:MaxFileBytes"] = "4096",
        ["Staff:RestrictedLimits:MaxLoans"] = "2",
        ["Staff:RestrictedLimits:MaxRenewals"] = "0"
    };

    private static IHost Host(Dictionary<string, string?> settings) => new HostBuilder()
        .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-characters",
            ["Jwt:AccessTokenMinutes"] = "10",
            ["Jwt:SecurityStampCacheSeconds"] = "5"
        }).AddInMemoryCollection(settings))
        .ConfigureServices((context, services) => services.AddStaffModule(context.Configuration))
        .Build();

    [Fact]
    public async Task The_module_binds_non_default_values_and_accepts_zero_renewals()
    {
        using var host = Host(Valid());
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<StaffOptions>>().Value;
        Assert.Equal(3, options.Import.MaxRows);
        Assert.Equal(4096, options.Import.MaxFileBytes);
        Assert.Equal(2, options.RestrictedLimits.MaxLoans);
        Assert.Equal(0, options.RestrictedLimits.MaxRenewals);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Staff:Import:MaxRows")]
    [InlineData("Staff:Import:MaxFileBytes")]
    [InlineData("Staff:RestrictedLimits:MaxLoans")]
    [InlineData("Staff:RestrictedLimits:MaxRenewals")]
    public async Task Each_missing_key_prevents_host_start(string key)
    {
        var settings = Valid();
        settings.Remove(key);
        using var host = Host(settings);

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains(key, error.Message);
    }

    [Fact]
    public async Task A_missing_section_prevents_host_start()
    {
        using var host = Host(new());
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("Staff:Import:MaxRows", "0")]
    [InlineData("Staff:Import:MaxFileBytes", "0")]
    [InlineData("Staff:RestrictedLimits:MaxLoans", "-1")]
    [InlineData("Staff:RestrictedLimits:MaxRenewals", "-1")]
    public async Task Invalid_values_prevent_host_start(string key, string value)
    {
        var settings = Valid();
        settings[key] = value;
        using var host = Host(settings);

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains(key, error.Message);
    }
}
