using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Staff;

/// <summary>The user chip shows the name from the token, not one guessed from the email (MEM-02).</summary>
public class UserChipNameTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public async Task The_header_shows_the_member_name_when_it_differs_from_the_email()
    {
        var memberId = StaffSeedIds.StaffMembers.LusineKarapetyan;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            var member = await db.StaffMembers.SingleAsync(x => x.Id == memberId);
            member.UpdateProfile("Lucy", "Avagyan", member.CorporateEmail, member.Position, member.LineManagerEmail);
            await db.SaveChangesAsync();
        }

        var client = await UiTestClient.SignedInAsync(factory, LoginPage.Email(memberId), StaffSeedData.Password);
        var html = await client.GetStringAsync("/my-library");

        Assert.Contains("<span class=\"user-name\">Lucy Avagyan<span", html);
        Assert.DoesNotContain("Lusine Karapetyan<span", html);
    }
}