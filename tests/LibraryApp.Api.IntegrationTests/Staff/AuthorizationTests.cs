using System.Net;
using System.Net.Http.Headers;
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
[Route("test-authorization")]
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
        .Issue(new LoginAccount(Guid.NewGuid(), "anna@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, roles))
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/test-authorization/default")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_without_attributes_accepts_any_valid_token()
    {
        Assert.Equal(HttpStatusCode.OK, (await Get("/test-authorization/default", Token(StaffRoles.StaffMember))).StatusCode);
    }

    [Fact]
    public async Task Role_endpoint_returns_403_for_another_role_and_200_for_the_role()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/test-authorization/librarian", Token(StaffRoles.StaffMember))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get("/test-authorization/librarian", Token(StaffRoles.Librarian))).StatusCode);
    }

    [Fact]
    public async Task Tampered_token_returns_401_without_error_details()
    {
        var parts = Token(StaffRoles.Librarian).Split('.');
        parts[2] = (parts[2][0] == 'A' ? 'B' : 'A') + parts[2][1..];

        var response = await Get("/test-authorization/librarian", string.Join('.', parts));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
    }
}