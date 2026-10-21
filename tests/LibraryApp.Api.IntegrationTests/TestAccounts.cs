using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests;

internal static class TestAccounts
{
    /// <summary>
    /// Uses a persisted account and its current stamp so endpoint tests exercise account validation too.
    /// Tests may override individual claims when checking how an endpoint handles incomplete identities.
    /// </summary>
    public static LoginAccount Account(this WebApplicationFactory<Program> factory, params string[] roles)
    {
        var memberId = roles.Contains(StaffRoles.LibraryAdministrator) ? StaffSeedIds.StaffMembers.LilitGrigoryan
            : roles.Contains(StaffRoles.Librarian) ? StaffSeedIds.StaffMembers.AramSargsyan
            : StaffSeedIds.StaffMembers.AnnaPetrosyan;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var account = (from staff in db.StaffMembers.AsNoTracking()
                       join user in db.Users.AsNoTracking() on staff.UserId equals user.Id
                       where staff.Id == memberId
                       select new { user.Id, user.Email, user.SecurityStamp, staff.Status }).Single();
        return new LoginAccount(account.Id, account.Email!, memberId, account.Status, roles, account.SecurityStamp!);
    }
}
