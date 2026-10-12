using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Seed.Staff;

/// <summary>
/// Staff demo data (SD-17): roles, departments (MEM-06), staff categories (MEM-04) and, per test person, a login
/// account in its role plus a StaffMember in its status. Idempotent: each row is looked up by its constant id first.
/// Runs inside the transaction opened by <see cref="DatabaseInitializer"/>.
/// </summary>
public sealed class StaffSeeder(
    LibraryDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IClock clock) : IModuleSeeder
{
    public int Order => 20;

    public string Module => "Staff";

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedRolesAsync();
        await SeedDepartmentsAsync(ct);
        await SeedCategoriesAsync(ct);

        foreach (var person in StaffSeedData.People)
            await SeedPersonAsync(person, ct);
    }

    /// <summary>
    /// The migration already creates the roles (<see cref="StaffRoleIds"/>); kept for a database migrated before that,
    /// and skipped when the role exists.
    /// </summary>
    private async Task SeedRolesAsync()
    {
        foreach (var (id, name) in StaffRoleIds.All)
        {
            if (!await roleManager.RoleExistsAsync(name))
                Ensure(await roleManager.CreateAsync(new IdentityRole<Guid>(name) { Id = id }), $"role {name}");
        }
    }

    private async Task SeedDepartmentsAsync(CancellationToken ct)
    {
        var existing = await db.Departments.Select(x => x.Id).ToListAsync(ct);

        foreach (var (id, name) in StaffSeedData.DepartmentList.Where(x => !existing.Contains(x.Id)))
            db.Departments.Add(new Department(id, name));
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var existing = await db.StaffCategories.Select(x => x.Id).ToListAsync(ct);

        foreach (var (id, name, maxLoans, loanDays, maxRenewals) in StaffSeedData.CategoryList.Where(x => !existing.Contains(x.Id)))
            db.StaffCategories.Add(new StaffCategory(id, name, maxLoans, loanDays, maxRenewals));
    }

    private async Task SeedPersonAsync(SeedPerson person, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(person.UserId.ToString());
        if (user is null)
        {
            user = new AppUser { Id = person.UserId, UserName = person.Email, Email = person.Email, EmailConfirmed = true };
            Ensure(await userManager.CreateAsync(user, StaffSeedData.Password), $"user {person.Email}");
        }

        if (!await userManager.IsInRoleAsync(user, person.Role))
            Ensure(await userManager.AddToRoleAsync(user, person.Role), $"role of {person.Email}");

        if (await db.StaffMembers.AnyAsync(x => x.Id == person.StaffMemberId, ct))
            return;

        var member = new StaffMember(
            person.StaffMemberId, person.UserId, person.EmployeeId, person.FirstName, person.LastName, person.Email,
            person.DepartmentId, person.Position, person.LineManagerEmail, person.CategoryId,
            clock.UtcNow.AddDays(-person.JoinedDaysAgo));

        if (person.Status == StaffMemberStatus.Deactivated)
            member.Deactivate();
        else if (person.Status != StaffMemberStatus.Active)
            member.ChangeStatus(person.Status);

        db.StaffMembers.Add(member);
    }

    private static void Ensure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Staff seed failed for {what}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}