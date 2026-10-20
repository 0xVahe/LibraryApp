using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

/// <summary>Migrates a throw-away database and runs no seeder: what every non-Development environment gets.</summary>
public sealed class MigratedWithoutSeedFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; } = BuildServices(TestDatabase.NewDatabase());

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(options => options.UseLibrarySqlServer(connectionString));
        services.AddLibraryIdentity();
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

public class StaffRolesMigrationTests(MigratedWithoutSeedFixture fixture) : IClassFixture<MigratedWithoutSeedFixture>
{
    [Fact]
    public async Task Migration_creates_the_three_roles_with_fixed_ids()
    {
        using var scope = fixture.Services.CreateScope();
        var roles = await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Roles.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.NormalizedName })
            .ToListAsync();

        var expected = StaffRoleIds.All
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, Name = (string?)x.Name, NormalizedName = (string?)x.Name.ToUpperInvariant() })
            .ToList();

        Assert.Equal(expected, roles);
    }

    [Theory]
    [InlineData(StaffRoles.LibraryAdministrator)]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.StaffMember)]
    public async Task Role_manager_finds_each_role_by_name(string role)
    {
        using var scope = fixture.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        Assert.True(await roles.RoleExistsAsync(role));
    }

    /// <summary>What enrolment does: before the fix this failed with "Role STAFFMEMBER does not exist" (500).</summary>
    [Fact]
    public async Task A_new_account_can_be_put_in_a_role_without_the_seed()
    {
        using var scope = fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"role-check-{Guid.NewGuid():N}@ameriabank.am";
        var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email };

        Assert.True((await users.CreateAsync(user, "RoleCheck2026")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, StaffRoles.StaffMember)).Succeeded);
        Assert.Equal(StaffRoles.StaffMember, Assert.Single(await users.GetRolesAsync(user)));
    }
}