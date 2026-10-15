using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>NFR-02: new endpoints join this check automatically through the real host's route table.</summary>
public class EndpointSecurityTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task Every_endpoint_requires_a_token_except_login_and_health_checks()
    {
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>().ToArray();
        Assert.NotEmpty(endpoints);
        var anonymousRoutes = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var path = RequestPath(endpoint);
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (anonymous)
            {
                Assert.Contains(path, new[] { "/api/auth/login", "/health/live", "/health/ready" });
                if (path == "/api/auth/login")
                    Assert.Equal(new[] { "POST" }, methods);
                anonymousRoutes.Add(path);
                continue;
            }

            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            foreach (var method in methods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                using var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
                    $"{method} {path} returned {(int)response.StatusCode}; expected 401 without a token.");
            }
        }

        Assert.Equal(new[] { "/api/auth/login", "/health/live", "/health/ready" },
            anonymousRoutes.Order(StringComparer.Ordinal));
    }

    private static string RequestPath(RouteEndpoint endpoint) => "/" + string.Join("/",
        endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => parameter.Default?.ToString()
                ?? (parameter.ParameterPolicies.Any(policy => policy.Content == "guid")
                    ? "d219ee3e-062a-4d97-bf74-b9c6f3d8af74"
                    : "1"),
            _ => throw new InvalidOperationException($"Provide a request value for route {endpoint.RoutePattern.RawText}."),
        }))));
}
