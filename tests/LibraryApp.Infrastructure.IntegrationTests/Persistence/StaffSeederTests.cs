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
        Assert.Equal(30, StaffSeedData.People.Count);
        Assert.Equal(30, await db.StaffMembers.CountAsync());
        Assert.Equal(30, await db.Users.CountAsync());
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
    [InlineData("narek.simonyan@ameriabank.am", StaffRoles.StaffMember)]
    [InlineData("eva.hovsepyan@ameriabank.am", StaffRoles.StaffMember)]
    [InlineData("tigran.melikyan@ameriabank.am", StaffRoles.StaffMember)]
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

    [Fact]
    public async Task Every_department_has_five_staff_members_and_all_three_categories()
    {
        using var scope = fixture.Services.CreateScope();
        var db = Get<LibraryDbContext>(scope);
        var departments = await db.Departments.AsNoTracking().ToListAsync();
        var members = await db.StaffMembers.AsNoTracking().ToListAsync();
        var categoryIds = await db.StaffCategories
            .Select(x => x.Id)
            .ToListAsync();

        Assert.Equal(6, departments.Count);
        Assert.Equal(3, categoryIds.Count);

        foreach (var department in departments)
        {
            var departmentMembers = members
                .Where(x => x.DepartmentId == department.Id)
                .ToList();

            Assert.Equal(5, departmentMembers.Count);

            foreach (var categoryId in categoryIds)
                Assert.Contains(departmentMembers, x => x.StaffCategoryId == categoryId);
        }
    }

    [Fact]
    public async Task The_twenty_two_additional_staff_members_are_active_with_corporate_emails()
    {
        using var scope = fixture.Services.CreateScope();
        var db = Get<LibraryDbContext>(scope);

        Guid[] originalIds =
        [
            StaffSeedIds.StaffMembers.LilitGrigoryan,
            StaffSeedIds.StaffMembers.AramSargsyan,
            StaffSeedIds.StaffMembers.AnnaPetrosyan,
            StaffSeedIds.StaffMembers.DavitHakobyan,
            StaffSeedIds.StaffMembers.MariamAvetisyan,
            StaffSeedIds.StaffMembers.GorMkrtchyan,
            StaffSeedIds.StaffMembers.SonaVardanyan,
            StaffSeedIds.StaffMembers.HaykManukyan,
        ];

        var additionalMembers = await db.StaffMembers
            .AsNoTracking()
            .Where(x => !originalIds.Contains(x.Id))
            .ToListAsync();

        Assert.Equal(22, additionalMembers.Count);

        Assert.All(additionalMembers, member =>
        {
            Assert.Equal(StaffMemberStatus.Active, member.Status);
            Assert.EndsWith("@ameriabank.am", member.CorporateEmail);
        });
    }

    [Theory]
    [InlineData(1, "EMP-90001", "lilit.grigoryan@ameriabank.am", StaffMemberStatus.Active)]
    [InlineData(2, "EMP-90002", "aram.sargsyan@ameriabank.am", StaffMemberStatus.Active)]
    [InlineData(3, "EMP-10001", "anna.petrosyan@ameriabank.am", StaffMemberStatus.Active)]
    [InlineData(4, "EMP-10002", "davit.hakobyan@ameriabank.am", StaffMemberStatus.Active)]
    [InlineData(5, "EMP-10003", "mariam.avetisyan@ameriabank.am", StaffMemberStatus.Active)]
    [InlineData(6, "EMP-10004", "gor.mkrtchyan@ameriabank.am", StaffMemberStatus.Restricted)]
    [InlineData(7, "EMP-10005", "sona.vardanyan@ameriabank.am", StaffMemberStatus.Suspended)]
    [InlineData(8, "EMP-10006", "hayk.manukyan@ameriabank.am", StaffMemberStatus.Deactivated)]
    public async Task Original_staff_members_keep_their_ids_employee_ids_and_statuses(
        int number,
        string employeeId,
        string email,
        StaffMemberStatus status)
    {
        using var scope = fixture.Services.CreateScope();
        var db = Get<LibraryDbContext>(scope);
        var member = await db.StaffMembers
            .AsNoTracking()
            .SingleAsync(x => x.CorporateEmail == email);

        var expectedStaffMemberId = new Guid($"20000000-0000-0000-0003-{number:D12}");
        var expectedUserId = new Guid($"20000000-0000-0000-0004-{number:D12}");

        Assert.Equal(expectedStaffMemberId, member.Id);
        Assert.Equal(expectedUserId, member.UserId);
        Assert.Equal(employeeId, member.EmployeeId);
        Assert.Equal(status, member.Status);
    }
}