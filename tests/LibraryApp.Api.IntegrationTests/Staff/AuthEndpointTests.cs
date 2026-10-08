using System.Net;
using System.Net.Http.Json;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

public class AuthEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Password = "Passw0rd1";

    private Task<HttpResponseMessage> Login(string email, string password = Password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task Gmail_address_returns_400()
    {
        var response = await Login("anna@gmail.com");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_corporate_email_returns_401()
    {
        var response = await Login($"nobody.{Guid.NewGuid():N}@ameriabank.am");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_credentials_return_a_token_with_the_role()
    {
        var email = $"librarian.{Guid.NewGuid():N}@ameriabank.am";
        using (var scope = factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(StaffRoles.Librarian))
                await roles.CreateAsync(new IdentityRole<Guid>(StaffRoles.Librarian) { Id = Guid.NewGuid() });

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, StaffRoles.Librarian)).Succeeded);
        }

        var response = await Login(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("Bearer", body!.TokenType);
        Assert.Equal(StaffRoles.Librarian, Assert.Single(body.Roles));
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
    }
}