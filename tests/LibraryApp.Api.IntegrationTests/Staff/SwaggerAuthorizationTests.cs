using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public sealed class SwaggerAuthorizationFactory : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddControllers().AddApplicationPart(typeof(TestSwaggerAuthorizationController).Assembly);
}

[ApiController]
[Authorize]
[Route("test-swagger-authorization")]
public sealed class TestSwaggerAuthorizationController : ControllerBase
{
    [HttpGet("anonymous")]
    [AllowAnonymous]
    public IActionResult Anonymous() => Ok();

    [HttpGet("protected")]
    public IActionResult Protected() => Ok();
}

public class SwaggerAuthorizationTests(SwaggerAuthorizationFactory factory) : IClassFixture<SwaggerAuthorizationFactory>
{
    [Fact]
    public async Task Bearer_authentication_is_available_without_a_global_security_requirement()
    {
        using var client = factory.CreateClient();
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        Assert.False(swagger.TryGetProperty("security", out _));
        var bearer = swagger.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Theory]
    [InlineData("/api/auth/login", "post")]
    [InlineData("/test-swagger-authorization/anonymous", "get")]
    public async Task Anonymous_operations_have_no_security_requirement(string path, string method)
    {
        var operation = await Operation(path, method);

        Assert.False(operation.TryGetProperty("security", out _));
    }

    [Theory]
    [InlineData("/api/staff-members", "get")]
    [InlineData("/api/staff-members", "post")]
    [InlineData("/api/departments", "get")]
    [InlineData("/api/books", "get")]
    [InlineData("/test-swagger-authorization/protected", "get")]
    public async Task Protected_operations_require_the_bearer_scheme(string path, string method)
    {
        var operation = await Operation(path, method);

        var requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());
        var scheme = Assert.Single(requirement.EnumerateObject());
        Assert.Equal("Bearer", scheme.Name);
        Assert.Empty(scheme.Value.EnumerateArray());
    }

    private async Task<JsonElement> Operation(string path, string method)
    {
        using var client = factory.CreateClient();
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        return swagger.GetProperty("paths").GetProperty(path).GetProperty(method);
    }
}
