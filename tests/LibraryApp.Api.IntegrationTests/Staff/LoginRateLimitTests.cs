using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class LoginRateLimitFactory : ApiHostFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("LoginRateLimit:PermitLimit", "2");
    }
}

public class LoginRateLimitTests(LoginRateLimitFactory factory) : IClassFixture<LoginRateLimitFactory>
{
    [Fact]
    public async Task Third_login_in_the_window_returns_429()
    {
        var client = factory.CreateClient();
        var request = new { email = $"nobody.{Guid.NewGuid():N}@ameriabank.am", password = "WrongPass1" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", request)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("too_many_requests", body.GetProperty("code").GetString());
    }
}