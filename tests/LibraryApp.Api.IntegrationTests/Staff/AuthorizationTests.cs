using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>The real API with a test controller: bearer tokens and roles end to end (NFR-02, NFR-03).</summary>
public sealed class AuthorizationFactory : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddControllers().AddApplicationPart(typeof(TestAuthorizationController).Assembly);
}

[ApiController]
[Route("api/test-authorization")]
public sealed class TestAuthorizationController : ControllerBase
{
    /// <summary>No attribute: protected by RequireAuthorization() on MapControllers (NFR-02).</summary>
    [HttpGet("default")]
    public IActionResult Default() => Ok();

    [HttpGet("librarian")]
    [Authorize(Roles = StaffRoles.Librarian)]
    public IActionResult Librarian() => Ok();
}

public class AuthorizationTests(AuthorizationFactory factory) : IClassFixture<AuthorizationFactory>
{
    private string Token(params string[] roles) => factory.Services.GetRequiredService<IAccessTokenIssuer>()
        .Issue(factory.Account(roles))
        .Token;

    private Task<HttpResponseMessage> Get(string path, string? token = null)
    {
        var client = factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(path);
    }

    [Fact]
    public async Task Endpoint_without_attributes_returns_401_without_a_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/test-authorization/default")).StatusCode);
    }

    [Theory]
    [InlineData(false, "application/json")]
    [InlineData(false, "text/plain")]
    [InlineData(false, "application/xml")]
    [InlineData(true, "application/json")]
    [InlineData(true, "text/plain")]
    [InlineData(true, "application/xml")]
    public async Task Middleware_errors_have_standard_codes_for_any_accept_header(bool wrongRole, string accept)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        if (wrongRole)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(StaffRoles.StaffMember));

        var response = await client.GetAsync("/api/test-authorization/librarian");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var status = wrongRole ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        var code = wrongRole ? "forbidden" : "unauthorized";

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal($"urn:library:error:{code}", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        if (!wrongRole)
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task Endpoint_without_attributes_accepts_any_valid_token()
    {
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/test-authorization/default", Token(StaffRoles.StaffMember))).StatusCode);
    }

    [Fact]
    public async Task Role_endpoint_returns_403_for_another_role_and_200_for_the_role()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/test-authorization/librarian", Token(StaffRoles.StaffMember))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/test-authorization/librarian", Token(StaffRoles.Librarian))).StatusCode);
    }

    [Fact]
    public async Task Tampered_token_returns_401_without_error_details()
    {
        var parts = Token(StaffRoles.Librarian).Split('.');
        parts[2] = (parts[2][0] == 'A' ? 'B' : 'A') + parts[2][1..];

        var response = await Get("/api/test-authorization/librarian", string.Join('.', parts));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("code").GetString());
    }
}
