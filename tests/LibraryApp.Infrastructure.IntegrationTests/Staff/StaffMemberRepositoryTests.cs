using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Infrastructure.IntegrationTests.Persistence;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Staff;

/// <summary>Runs the name query on a migrated, seeded database.</summary>
public class StaffMemberRepositoryTests(StaffSeederFixture fixture) : IClassFixture<StaffSeederFixture>
{
    [Fact]
    public async Task GetNames_reads_first_and_last_name_and_leaves_out_unknown_ids()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var repository = new StaffMemberRepository(db);

        var names = await repository.GetNamesAsync(
            [StaffSeedIds.StaffMembers.LilitGrigoryan, StaffSeedIds.StaffMembers.AnnaPetrosyan, Guid.NewGuid()],
            CancellationToken.None);

        Assert.Equal(2, names.Count);
        Assert.Equal("Lilit Grigoryan", names[StaffSeedIds.StaffMembers.LilitGrigoryan]);
        Assert.Equal("Anna Petrosyan", names[StaffSeedIds.StaffMembers.AnnaPetrosyan]);
        Assert.Empty(db.ChangeTracker.Entries());
    }
    
    [Fact]
    public async Task Find_reads_a_member_by_employee_id_and_by_email()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var repository = new StaffMemberRepository(db);

        var byId = await repository.FindByEmployeeIdAsync("emp-10001", CancellationToken.None);
        var byEmail = await repository.FindByCorporateEmailAsync(byId!.CorporateEmail, CancellationToken.None);

        Assert.Equal(StaffSeedIds.StaffMembers.AnnaPetrosyan, byId.Id);
        Assert.Equal("Anna Petrosyan", byId.FullName);
        Assert.Equal(byId, byEmail);
        Assert.Null(await repository.FindByEmployeeIdAsync("EMP-00000", CancellationToken.None));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    
    [Fact]
    public async Task GetContacts_reads_name_and_both_emails_and_leaves_out_unknown_ids()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var repository = new StaffMemberRepository(db);
        var anna = StaffSeedData.People.Single(x => x.StaffMemberId == StaffSeedIds.StaffMembers.AnnaPetrosyan);
        var lilit = StaffSeedData.People.Single(x => x.StaffMemberId == StaffSeedIds.StaffMembers.LilitGrigoryan);

        var contacts = await repository.GetContactsAsync([anna.StaffMemberId, lilit.StaffMemberId, Guid.NewGuid()], CancellationToken.None);

        Assert.Equal(2, contacts.Count);
        Assert.Equal(new StaffContact(anna.StaffMemberId, "Anna Petrosyan", anna.Email, anna.LineManagerEmail), contacts[anna.StaffMemberId]);
        Assert.Null(contacts[lilit.StaffMemberId].LineManagerEmail);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}