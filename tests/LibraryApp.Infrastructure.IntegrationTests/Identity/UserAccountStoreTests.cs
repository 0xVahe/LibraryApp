using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

/// <summary>A migrated throw-away database with Identity; every test creates its own account.</summary>
public sealed class UserAccountStoreFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; } = BuildServices(TestDatabase.NewConnectionString());

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(options => options.UseLibrarySqlServer(connectionString));
        services.AddLibraryIdentity();
        services.AddSingleton<IClock>(new FixedClock(DateTime.UtcNow));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<UserAccountStore>();
        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();

        await Services.DisposeAsync();
    }
}

public class UserAccountStoreTests(UserAccountStoreFixture fixture) : IClassFixture<UserAccountStoreFixture>
{
    private static readonly string Password = $"Pw1{Guid.NewGuid():N}";

    private async Task<AppUser> CreateUserAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var email = $"user.{Guid.NewGuid():N}@ameriabank.am";
        var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email };
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(user, Password))
            .Succeeded);
        return user;
    }

    private async Task AddPasswordChangeMarkerAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        var result = await users.AddClaimAsync(user, new System.Security.Claims.Claim(
            LibraryClaimTypes.PasswordChangeRequired, bool.TrueString));
        Assert.True(result.Succeeded);
    }

    /// <summary>Own scope per call, like one HTTP request each.</summary>
    private async Task<LoginAccount?> VerifyAsync(string email, string password)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserAccountStore>()
            .VerifyCredentialsAsync(email, password, CancellationToken.None);
    }

    private async Task<AppUser> ReloadAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>()
            .FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        return user;
    }

    [Fact]
    public async Task Right_password_returns_the_account()
    {
        var user = await CreateUserAsync();

        var account = await VerifyAsync(user.Email!, Password);

        Assert.NotNull(account);
        Assert.Equal(user.Id, account.UserId);
    }

    [Fact]
    public async Task Unknown_email_returns_null()
    {
        Assert.Null(await VerifyAsync($"nobody.{Guid.NewGuid():N}@ameriabank.am", Password));
    }

    [Fact]
    public async Task Wrong_password_is_counted_and_the_right_one_resets_the_count()
    {
        var user = await CreateUserAsync();

        Assert.Null(await VerifyAsync(user.Email!, "WrongPass1"));
        Assert.Equal(1, (await ReloadAsync(user.Id)).AccessFailedCount);

        Assert.NotNull(await VerifyAsync(user.Email!, Password));
        Assert.Equal(0, (await ReloadAsync(user.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task Parallel_wrong_passwords_lock_the_account()
    {
        var user = await CreateUserAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => VerifyAsync(user.Email!, "WrongPass1")));

        var stored = await ReloadAsync(user.Id);
        Assert.NotNull(stored.LockoutEnd);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.True(stored.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.Null(await VerifyAsync(user.Email!, Password));
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected_and_counted()
    {
        var user = await CreateUserAsync();

        Assert.False(await VerifyCurrentAsync(user.Id, "WrongPass1"));

        Assert.Equal(1, (await ReloadAsync(user.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task Right_current_password_is_accepted()
    {
        var user = await CreateUserAsync();

        Assert.True(await VerifyCurrentAsync(user.Id, Password));
    }

    [Fact]
    public async Task Change_sets_the_password_and_removes_the_first_login_marker()
    {
        var user = await CreateUserAsync();
        await AddPasswordChangeMarkerAsync(user.Id);

        Assert.True(await ChangeAsync(user.Id, Password, "NewSecurePass9!"));

        using var scope = fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var reloaded = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(reloaded);
        Assert.True(await users.CheckPasswordAsync(reloaded, "NewSecurePass9!"));
        Assert.DoesNotContain(await users.GetClaimsAsync(reloaded), claim =>
            claim.Type == LibraryClaimTypes.PasswordChangeRequired && claim.Value == bool.TrueString);
    }

    [Fact]
    public async Task Change_returns_false_when_the_new_password_breaks_the_rules()
    {
        var user = await CreateUserAsync();

        Assert.False(await ChangeAsync(user.Id, Password, "short"));
    }

    private async Task<bool> VerifyCurrentAsync(Guid userId, string password)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserAccountStore>()
            .VerifyCurrentPasswordAsync(userId, password, CancellationToken.None);
    }

    private async Task<bool> ChangeAsync(Guid userId, string currentPassword, string newPassword)
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<UserAccountStore>();
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(token =>
            store.ChangePasswordAsync(userId, currentPassword, newPassword, token));
    }


}
