using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.IntegrationTests.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>Migrates a throw-away database and runs the Staff seeder twice (the second run must add nothing).</summary>
public sealed class StaffSeederFixture : IAsyncLifetime
{
    public static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    public ServiceProvider Services { get; } = BuildServices(TestDatabase.NewConnectionString());

    private static ServiceProvider BuildServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LibraryDbContext>(options => options.UseLibrarySqlServer(connectionString));
        services.AddLibraryIdentity();
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<StaffSeeder>();
        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();

        await SeedAsync();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<StaffSeeder>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(token => seeder.SeedAsync(token));
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();

        await Services.DisposeAsync();
    }
}

public class StaffSeederTests(StaffSeederFixture fixture) : IClassFixture<StaffSeederFixture>
{
    private T Get<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Seeds_everything_exactly_once_after_two_runs()
    {
        using var scope = fixture.Services.CreateScope();
        var db = Get<LibraryDbContext>(scope);

        Assert.Equal(StaffRoleIds.All.Count, await db.Roles.CountAsync());
        Assert.Equal(6, await db.Departments.CountAsync());
        Assert.Equal(3, await db.StaffCategories.CountAsync());
        Assert.Equal(StaffSeedData.People.Count, await db.StaffMembers.CountAsync());
        Assert.Equal(StaffSeedData.People.Count, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Staff_categories_have_the_default_limits()
    {
        using var scope = fixture.Services.CreateScope();
        var employee = await Get<LibraryDbContext>(scope).StaffCategories.SingleAsync(x => x.Id == StaffSeedIds.Categories.Employee);

        Assert.Equal((3, 21, 1), (employee.MaxLoans, employee.LoanDays, employee.MaxRenewals));
    }

    [Theory]
    [InlineData("lilit.grigoryan@ameriabank.am", StaffRoles.LibraryAdministrator)]
    [InlineData("aram.sargsyan@ameriabank.am", StaffRoles.Librarian)]
    [InlineData("anna.petrosyan@ameriabank.am", StaffRoles.StaffMember)]
    public async Task Accounts_have_the_dev_password_and_their_role(string email, string role)
    {
        using var scope = fixture.Services.CreateScope();
        var users = Get<UserManager<AppUser>>(scope);
        var user = await users.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user, StaffSeedData.Password));
        Assert.Equal(role, Assert.Single(await users.GetRolesAsync(user)));
    }

    [Fact]
    public async Task Every_StaffMember_is_linked_to_its_account_and_has_its_status()
    {
        using var scope = fixture.Services.CreateScope();
        var members = await Get<LibraryDbContext>(scope).StaffMembers.AsNoTracking().ToDictionaryAsync(x => x.Id);

        foreach (var person in StaffSeedData.People)
        {
            var member = members[person.StaffMemberId];
            Assert.Equal(person.UserId, member.UserId);
            Assert.Equal(person.Email, member.CorporateEmail);
            Assert.Equal(person.Status, member.Status);
            Assert.Equal(StaffSeederFixture.Now.AddDays(-person.JoinedDaysAgo), member.JoinedLibraryAtUtc);
        }
    }
}